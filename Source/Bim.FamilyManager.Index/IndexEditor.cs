using Microsoft.Data.Sqlite;

namespace Bim.FamilyManager.Index;

/// <summary>
///     Edits the user-owned annotations of the family index: favorites and tags.
/// </summary>
/// <remarks>
///     Favorites and tags persist in the index database, never in the family files, so they survive
///     Revit restarts and re-scans (they reference families by id, and family rows are stable across
///     incremental scans). Removing a family from disk removes its annotations via cascade.
/// </remarks>
public sealed class IndexEditor
{
    private readonly FamilyIndex _index;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndexEditor" /> class.
    /// </summary>
    /// <param name="index">The index to edit.</param>
    public IndexEditor(FamilyIndex index)
    {
        _index = index;
    }

    /// <summary>
    ///     Marks or unmarks a family as favorite.
    /// </summary>
    /// <param name="familyId">The database identifier of the family.</param>
    /// <param name="isFavorite"><c>true</c> to mark as favorite; <c>false</c> to unmark.</param>
    public void SetFavorite(long familyId, bool isFavorite)
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = isFavorite
            ? "INSERT OR IGNORE INTO favorites (family_id) VALUES ($family);"
            : "DELETE FROM favorites WHERE family_id = $family;";
        command.Parameters.AddWithValue("$family", familyId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    ///     Determines whether a family is marked as favorite.
    /// </summary>
    /// <param name="familyId">The database identifier of the family.</param>
    /// <returns><c>true</c> if the family is a favorite; otherwise <c>false</c>.</returns>
    public bool IsFavorite(long familyId)
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM favorites WHERE family_id = $family;";
        command.Parameters.AddWithValue("$family", familyId);

        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    ///     Adds a tag to a family, creating the tag on first use.
    /// </summary>
    /// <param name="familyId">The database identifier of the family.</param>
    /// <param name="tag">The tag name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tag" /> is null or whitespace.</exception>
    public void AddTag(long familyId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException(@"Tag cannot be null or empty.", nameof(tag));
        }

        var name = tag.Trim();

        using var connection = _index.OpenConnection();
        using var transaction = connection.BeginTransaction();

        long tagId;
        using (var upsertTag = connection.CreateCommand())
        {
            upsertTag.CommandText =
                """
                INSERT INTO tags (name) VALUES ($name)
                ON CONFLICT(name) DO UPDATE SET name = excluded.name
                RETURNING id;
                """;
            upsertTag.Parameters.AddWithValue("$name", name);
            tagId = (long)upsertTag.ExecuteScalar()!;
        }

        using (var link = connection.CreateCommand())
        {
            link.CommandText = "INSERT OR IGNORE INTO family_tags (family_id, tag_id) VALUES ($family, $tag);";
            link.Parameters.AddWithValue("$family", familyId);
            link.Parameters.AddWithValue("$tag", tagId);
            link.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    ///     Removes a tag from a family. Tags that are no longer used by any family are deleted.
    /// </summary>
    /// <param name="familyId">The database identifier of the family.</param>
    /// <param name="tag">The tag name.</param>
    public void RemoveTag(long familyId, string tag)
    {
        using var connection = _index.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var unlink = connection.CreateCommand())
        {
            unlink.CommandText =
                "DELETE FROM family_tags WHERE family_id = $family AND tag_id = (SELECT id FROM tags WHERE name = $name);";
            unlink.Parameters.AddWithValue("$family", familyId);
            unlink.Parameters.AddWithValue("$name", tag.Trim());
            unlink.ExecuteNonQuery();
        }

        using (var prune = connection.CreateCommand())
        {
            prune.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM family_tags);";
            prune.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    ///     Gets the tags of a family, ordered alphabetically.
    /// </summary>
    /// <param name="familyId">The database identifier of the family.</param>
    /// <returns>The tag names.</returns>
    public IReadOnlyList<string> GetTags(long familyId)
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.name FROM tags t
            JOIN family_tags ft ON ft.tag_id = t.id
            WHERE ft.family_id = $family
            ORDER BY t.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$family", familyId);

        return ReadStrings(command);
    }

    /// <summary>
    ///     Gets all tags present in the index, ordered alphabetically, for building filter lists.
    /// </summary>
    /// <returns>The tag names.</returns>
    public IReadOnlyList<string> GetAllTags()
    {
        using var connection = _index.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE;";

        return ReadStrings(command);
    }

    private static IReadOnlyList<string> ReadStrings(SqliteCommand command)
    {
        var results = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
