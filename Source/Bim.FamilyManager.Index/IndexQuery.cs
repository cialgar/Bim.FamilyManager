using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Bim.FamilyManager.Index;

/// <summary>
///     Queries the family index: full-text search plus combinable filters.
/// </summary>
/// <remarks>
///     Text queries run against the FTS5 table (name, folder, category and family type names) with
///     per-token prefix matching, so "cof" already finds "Coffee table2". An empty text query returns
///     families matching only the filters. Results are ordered by FTS rank when a text query is
///     present, otherwise by name.
/// </remarks>
public sealed class IndexQuery
{
    private readonly FamilyIndex _index;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndexQuery" /> class.
    /// </summary>
    /// <param name="index">The index to query.</param>
    public IndexQuery(FamilyIndex index)
    {
        _index = index;
    }

    /// <summary>
    ///     Searches the index for families matching the specified text and filters.
    /// </summary>
    /// <param name="text">The search text, or <c>null</c>/empty to match by filters only.</param>
    /// <param name="filter">Optional filters to combine with the text query.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>The matching families.</returns>
    public IReadOnlyList<FamilyRecord> Search(string? text, SearchFilter? filter = null, int limit = 200)
    {
        var sql = new StringBuilder(
            """
            SELECT f.id, f.path, f.name, f.folder, f.size, f.product_version, f.category,
                   f.category_key, f.omniclass, f.updated_utc
            FROM families f
            """);

        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        var match = BuildMatchExpression(text);
        if (match is not null)
        {
            sql.Append(" JOIN families_fts fts ON fts.rowid = f.id");
            conditions.Add("families_fts MATCH $match");
            parameters.Add(new SqliteParameter("$match", match));
        }

        if (filter?.Category is not null)
        {
            conditions.Add("f.category = $category");
            parameters.Add(new SqliteParameter("$category", filter.Category));
        }

        if (filter?.ProductVersion is not null)
        {
            conditions.Add("f.product_version = $version");
            parameters.Add(new SqliteParameter("$version", filter.ProductVersion));
        }

        if (filter?.FolderPrefix is not null)
        {
            conditions.Add("(f.folder = $folder OR f.folder LIKE $folderPrefix)");
            parameters.Add(new SqliteParameter("$folder", filter.FolderPrefix));
            parameters.Add(new SqliteParameter("$folderPrefix", filter.FolderPrefix + "\\%"));
        }

        if (filter?.FavoritesOnly == true)
        {
            conditions.Add("f.id IN (SELECT family_id FROM favorites)");
        }

        if (filter?.Tag is not null)
        {
            conditions.Add(
                "f.id IN (SELECT family_id FROM family_tags ft JOIN tags t ON t.id = ft.tag_id WHERE t.name = $tag)");
            parameters.Add(new SqliteParameter("$tag", filter.Tag));
        }

        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }

        sql.Append(match is not null ? " ORDER BY rank" : " ORDER BY f.name COLLATE NOCASE");
        sql.Append(" LIMIT $limit;");
        parameters.Add(new SqliteParameter("$limit", limit));

        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.Parameters.AddRange(parameters.ToArray());

        var results = new List<FamilyRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadFamilyRecord(reader));
        }

        return results;
    }

    /// <summary>
    ///     Materializes a <see cref="FamilyRecord" /> from the standard 10-column projection.
    /// </summary>
    /// <param name="reader">A reader positioned on a row of the standard projection.</param>
    /// <returns>The materialized record.</returns>
    private static FamilyRecord ReadFamilyRecord(SqliteDataReader reader)
    {
        DateTime? updated = null;
        if (!reader.IsDBNull(9) &&
            DateTime.TryParse(reader.GetString(9), CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            updated = parsed;
        }

        return new FamilyRecord
        {
            Id = reader.GetInt64(0),
            Path = reader.GetString(1),
            Name = reader.GetString(2),
            Folder = reader.GetString(3),
            Size = reader.GetInt64(4),
            ProductVersion = reader.IsDBNull(5) ? null : reader.GetString(5),
            Category = reader.IsDBNull(6) ? null : reader.GetString(6),
            CategoryKey = reader.IsDBNull(7) ? null : reader.GetString(7),
            OmniClassNumber = reader.IsDBNull(8) ? null : reader.GetString(8),
            Updated = updated
        };
    }

    /// <summary>
    ///     Gets the immediate child folders of the specified folder within a source.
    /// </summary>
    /// <param name="rootPath">The root directory of the family source.</param>
    /// <param name="parentFolder">The source-relative parent folder, or an empty string for the root.</param>
    /// <returns>The source-relative paths of the immediate child folders, ordered alphabetically.</returns>
    /// <remarks>
    ///     The hierarchy is reconstructed from the <c>folder</c> column, so only folders that (directly
    ///     or transitively) contain family files are returned — empty directories do not appear, which
    ///     is intentional for a family browser.
    /// </remarks>
    public IReadOnlyList<string> GetChildFolders(string rootPath, string parentFolder)
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT f.folder
            FROM families f
            JOIN sources s ON s.id = f.source_id
            WHERE s.root_path = $root AND f.folder != $parent AND ($parent = '' OR f.folder LIKE $prefix);
            """;
        command.Parameters.AddWithValue("$root", System.IO.Path.GetFullPath(rootPath));
        command.Parameters.AddWithValue("$parent", parentFolder);
        command.Parameters.AddWithValue("$prefix", parentFolder + "\\%");

        var children = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixLength = parentFolder.Length == 0 ? 0 : parentFolder.Length + 1;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var folder = reader.GetString(0);
            var remainder = folder[prefixLength..];
            var separator = remainder.IndexOf('\\');
            var childSegment = separator < 0 ? remainder : remainder[..separator];

            if (childSegment.Length > 0)
            {
                children.Add(parentFolder.Length == 0 ? childSegment : parentFolder + "\\" + childSegment);
            }
        }

        return children.ToList();
    }

    /// <summary>
    ///     Gets the families of the specified folder within a source.
    /// </summary>
    /// <param name="rootPath">The root directory of the family source.</param>
    /// <param name="folder">The source-relative folder, or an empty string for the root.</param>
    /// <param name="includeSubfolders">If <c>true</c>, includes families in all subfolders.</param>
    /// <returns>The matching families, ordered by name.</returns>
    public IReadOnlyList<FamilyRecord> GetFamilies(string rootPath, string folder, bool includeSubfolders)
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT f.id, f.path, f.name, f.folder, f.size, f.product_version, f.category,
                   f.category_key, f.omniclass, f.updated_utc
            FROM families f
            JOIN sources s ON s.id = f.source_id
            WHERE s.root_path = $root
              AND (f.folder = $folder OR ($includeSub AND ($folder = '' OR f.folder LIKE $prefix)))
            ORDER BY f.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$root", System.IO.Path.GetFullPath(rootPath));
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$includeSub", includeSubfolders);
        command.Parameters.AddWithValue("$prefix", folder + "\\%");

        var results = new List<FamilyRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadFamilyRecord(reader));
        }

        return results;
    }

    /// <summary>
    ///     Gets the distinct categories present in the index, for building filter lists.
    /// </summary>
    /// <returns>The categories, ordered alphabetically.</returns>
    public IReadOnlyList<string> GetCategories()
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT category FROM families WHERE category IS NOT NULL ORDER BY category;";

        var categories = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            categories.Add(reader.GetString(0));
        }

        return categories;
    }

    /// <summary>
    ///     Builds an FTS5 match expression with per-token prefix matching, or <c>null</c> for empty input.
    /// </summary>
    /// <param name="text">The raw search text.</param>
    /// <returns>The match expression, or <c>null</c> when the text contains no usable tokens.</returns>
    /// <remarks>
    ///     Tokens are quoted to neutralize FTS5 query syntax in user input (quotes, asterisks, boolean
    ///     operators), and each token gets a trailing <c>*</c> for prefix search.
    /// </remarks>
    private static string? BuildMatchExpression(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tokens = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(token => token.Replace("\"", string.Empty))
                         .Where(token => token.Length > 0)
                         .Select(token => $"\"{token}\"*")
                         .ToList();

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }
}

/// <summary>
///     Represents the combinable filters of a <see cref="IndexQuery.Search" /> call.
/// </summary>
public sealed record SearchFilter
{
    /// <summary>Gets the localized category to filter by, or <c>null</c> for all categories.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the product version to filter by, or <c>null</c> for all versions.</summary>
    public string? ProductVersion { get; init; }

    /// <summary>Gets the source-relative folder to restrict the search to (including subfolders), or <c>null</c> for all folders.</summary>
    public string? FolderPrefix { get; init; }

    /// <summary>Gets a value indicating whether only favorite families are returned.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>Gets the tag to filter by, or <c>null</c> for all tags.</summary>
    public string? Tag { get; init; }
}
