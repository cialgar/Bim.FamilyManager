using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Bim.FamilyManager.Rfa;
using Microsoft.Data.Sqlite;

namespace Bim.FamilyManager.Index;

/// <summary>
///     Performs incremental scans of a directory tree, keeping the family index in sync with the disk.
/// </summary>
/// <remarks>
///     A scan enumerates all family files (*.rfa, excluding Revit backup files) under the source root
///     and compares each file's last write time and size against the index. Only new or changed files
///     have their metadata re-extracted (via <see cref="PartAtomReader" />, which reads just the
///     required compound file sectors); unchanged files are not opened at all. Families whose files no
///     longer exist are removed. The whole scan runs in a single transaction, so readers never observe
///     a half-updated index.
///     Files whose metadata cannot be extracted are still indexed with their basic file information
///     (name, folder, size), so they remain findable by name; they are reported in
///     <see cref="ScanResult.FailedExtractions" />.
/// </remarks>
public sealed class IndexScanner
{
    private static readonly Regex BackupRegex = new(@"\.\d{4}\.rfa$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly FamilyIndex _index;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndexScanner" /> class.
    /// </summary>
    /// <param name="index">The index to keep in sync.</param>
    public IndexScanner(FamilyIndex index)
    {
        _index = index;
    }

    /// <summary>
    ///     Scans the specified source root and synchronizes the index with the current disk state.
    /// </summary>
    /// <param name="rootPath">The root directory of the family source.</param>
    /// <param name="progress">An optional progress sink, notified once per processed file.</param>
    /// <param name="cancellationToken">A cancellation token to observe while scanning.</param>
    /// <returns>A <see cref="ScanResult" /> describing what changed.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="rootPath" /> does not exist.</exception>
    public ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"The source root was not found: {rootPath}");
        }

        var stopwatch = Stopwatch.StartNew();
        _index.EnsureCreated();

        var fullRoot = Path.GetFullPath(rootPath);
        var diskFiles = EnumerateFamilyFiles(fullRoot, cancellationToken);

        using var connection = _index.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var sourceId = UpsertSource(connection, fullRoot);
        var known = LoadKnownFamilies(connection, sourceId);

        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var failed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < diskFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = diskFiles[i];
            progress?.Report(new ScanProgress(i + 1, diskFiles.Count, file.FullName));
            seen.Add(file.FullName);

            if (known.TryGetValue(file.FullName, out var existing) &&
                existing.MtimeUtcTicks == file.LastWriteTimeUtc.Ticks &&
                existing.Size == file.Length)
            {
                unchanged++;
                continue;
            }

            var info = PartAtomReader.Read(file.FullName);
            if (info is null)
            {
                failed.Add(file.FullName);
            }

            UpsertFamily(connection, sourceId, fullRoot, file, info);

            if (known.ContainsKey(file.FullName))
            {
                updated++;
            }
            else
            {
                added++;
            }
        }

        var removed = RemoveMissingFamilies(connection, sourceId, seen, known);
        UpdateLastScan(connection, sourceId);

        transaction.Commit();
        stopwatch.Stop();

        return new ScanResult
        {
            Added = added,
            Updated = updated,
            Removed = removed,
            Unchanged = unchanged,
            FailedExtractions = failed,
            Duration = stopwatch.Elapsed
        };
    }

    /// <summary>
    ///     Enumerates all family files under the root, tolerating inaccessible subdirectories.
    /// </summary>
    private static List<FileInfo> EnumerateFamilyFiles(string fullRoot, CancellationToken cancellationToken)
    {
        var result = new List<FileInfo>();
        var pending = new Stack<string>();
        pending.Push(fullRoot);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            try
            {
                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                {
                    pending.Push(subdirectory);
                }

                foreach (var file in Directory.EnumerateFiles(directory, "*.rfa"))
                {
                    if (!BackupRegex.IsMatch(file))
                    {
                        result.Add(new FileInfo(file));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories the user cannot read; they simply do not contribute families.
            }
            catch (IOException)
            {
            }
        }

        return result;
    }

    private static long UpsertSource(SqliteConnection connection, string fullRoot)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (root_path) VALUES ($root)
            ON CONFLICT(root_path) DO UPDATE SET root_path = excluded.root_path
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$root", fullRoot);

        return (long)command.ExecuteScalar()!;
    }

    private static Dictionary<string, (long Id, long MtimeUtcTicks, long Size)> LoadKnownFamilies(
        SqliteConnection connection, long sourceId)
    {
        var known = new Dictionary<string, (long, long, long)>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, id, mtime_utc_ticks, size FROM families WHERE source_id = $source;";
        command.Parameters.AddWithValue("$source", sourceId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            known[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }

        return known;
    }

    private static void UpsertFamily(SqliteConnection connection, long sourceId, string fullRoot, FileInfo file, PartAtomInfo? info)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var folder = Path.GetRelativePath(fullRoot, file.DirectoryName!);
        if (folder == ".")
        {
            folder = string.Empty;
        }

        long familyId;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO families (source_id, path, name, folder, size, mtime_utc_ticks,
                                      product_version, category, omniclass, updated_utc, indexed_at_utc)
                VALUES ($source, $path, $name, $folder, $size, $mtime, $version, $category, $omniclass, $updated, $indexedAt)
                ON CONFLICT(path) DO UPDATE SET
                    source_id = excluded.source_id,
                    name = excluded.name,
                    folder = excluded.folder,
                    size = excluded.size,
                    mtime_utc_ticks = excluded.mtime_utc_ticks,
                    product_version = excluded.product_version,
                    category = excluded.category,
                    omniclass = excluded.omniclass,
                    updated_utc = excluded.updated_utc,
                    indexed_at_utc = excluded.indexed_at_utc
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$source", sourceId);
            command.Parameters.AddWithValue("$path", file.FullName);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$folder", folder);
            command.Parameters.AddWithValue("$size", file.Length);
            command.Parameters.AddWithValue("$mtime", file.LastWriteTimeUtc.Ticks);
            command.Parameters.AddWithValue("$version", (object?)info?.ProductVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$category", (object?)info?.Category ?? DBNull.Value);
            command.Parameters.AddWithValue("$omniclass", (object?)info?.OmniClassNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", (object?)info?.Updated?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
            command.Parameters.AddWithValue("$indexedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            familyId = (long)command.ExecuteScalar()!;
        }

        using (var deleteSymbols = connection.CreateCommand())
        {
            deleteSymbols.CommandText = "DELETE FROM symbols WHERE family_id = $family;";
            deleteSymbols.Parameters.AddWithValue("$family", familyId);
            deleteSymbols.ExecuteNonQuery();
        }

        var symbolNames = info?.SymbolNames ?? [];
        foreach (var symbolName in symbolNames)
        {
            using var insertSymbol = connection.CreateCommand();
            insertSymbol.CommandText = "INSERT INTO symbols (family_id, name) VALUES ($family, $name);";
            insertSymbol.Parameters.AddWithValue("$family", familyId);
            insertSymbol.Parameters.AddWithValue("$name", symbolName);
            insertSymbol.ExecuteNonQuery();
        }

        using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.CommandText = "DELETE FROM families_fts WHERE rowid = $family;";
            deleteFts.Parameters.AddWithValue("$family", familyId);
            deleteFts.ExecuteNonQuery();
        }

        using (var insertFts = connection.CreateCommand())
        {
            insertFts.CommandText =
                "INSERT INTO families_fts (rowid, name, folder, category, symbols) VALUES ($family, $name, $folder, $category, $symbols);";
            insertFts.Parameters.AddWithValue("$family", familyId);
            insertFts.Parameters.AddWithValue("$name", name);
            insertFts.Parameters.AddWithValue("$folder", folder);
            insertFts.Parameters.AddWithValue("$category", (object?)info?.Category ?? DBNull.Value);
            insertFts.Parameters.AddWithValue("$symbols", string.Join(' ', symbolNames));
            insertFts.ExecuteNonQuery();
        }
    }

    private static int RemoveMissingFamilies(SqliteConnection connection, long sourceId, HashSet<string> seen,
                                             Dictionary<string, (long Id, long MtimeUtcTicks, long Size)> known)
    {
        var removed = 0;
        foreach (var (path, entry) in known)
        {
            if (seen.Contains(path))
            {
                continue;
            }

            using (var deleteFts = connection.CreateCommand())
            {
                deleteFts.CommandText = "DELETE FROM families_fts WHERE rowid = $family;";
                deleteFts.Parameters.AddWithValue("$family", entry.Id);
                deleteFts.ExecuteNonQuery();
            }

            using (var deleteFamily = connection.CreateCommand())
            {
                deleteFamily.CommandText = "DELETE FROM families WHERE id = $family;";
                deleteFamily.Parameters.AddWithValue("$family", entry.Id);
                deleteFamily.ExecuteNonQuery();
            }

            removed++;
        }

        return removed;
    }

    private static void UpdateLastScan(SqliteConnection connection, long sourceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sources SET last_scan_utc = $now WHERE id = $source;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$source", sourceId);
        command.ExecuteNonQuery();
    }
}
