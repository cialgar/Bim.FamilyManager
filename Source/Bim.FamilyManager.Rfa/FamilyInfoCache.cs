using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Provides a persistent, per-file cache for the display information of family files: card metadata
///     and the native thumbnail (PNG).
/// </summary>
/// <remarks>
///     Extracting this information requires opening the family file, which is expensive for large
///     libraries. This cache stores each extraction result on disk so that subsequent sessions can
///     render the family cards without touching the family files at all.
///     Cache entries are keyed by the family file path and its last write time and size. When the family
///     file changes, the key changes as well, the stale entries are removed on the next store, and the
///     information is extracted again. The cache is self-healing: corrupted or externally deleted
///     entries are treated as cache misses and regenerated.
///     Each entry consists of a metadata file (<c>{key}.json</c>) and, when the family has a preview,
///     a thumbnail file (<c>{key}.png</c>). The metadata file is written last and acts as the marker of
///     a complete entry.
/// </remarks>
public sealed class FamilyInfoCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _cacheDirectory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FamilyInfoCache" /> class.
    /// </summary>
    /// <param name="cacheDirectory">The directory in which cache entries are stored. Created on first write.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cacheDirectory" /> is null or whitespace.</exception>
    public FamilyInfoCache(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException(@"Cache directory cannot be null or empty.", nameof(cacheDirectory));
        }

        _cacheDirectory = cacheDirectory;
    }

    /// <summary>
    ///     Creates a cache instance using the default location <c>%AppData%\FamilyManager\thumbnails</c>.
    /// </summary>
    /// <returns>A <see cref="FamilyInfoCache" /> rooted at the default cache directory.</returns>
    public static FamilyInfoCache CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new FamilyInfoCache(Path.Combine(appData, "FamilyManager", "thumbnails"));
    }

    /// <summary>
    ///     Attempts to retrieve the cached information for the specified family file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <param name="info">The cached information, or <c>null</c> when the method returns <c>false</c>.</param>
    /// <returns>
    ///     <c>true</c> if a complete cache entry exists for the current state (path, last write time,
    ///     size) of the family file; otherwise <c>false</c>. A missing family file, a missing entry, a
    ///     stale entry or a corrupted entry all result in <c>false</c>.
    /// </returns>
    public bool TryGet(string familyFilePath, out CachedFamilyInfo? info)
    {
        info = null;

        var fileInfo = new FileInfo(familyFilePath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        var entryPath = GetEntryBasePath(fileInfo);
        var metadataPath = entryPath + ".json";
        var thumbnailPath = entryPath + ".png";

        try
        {
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            var metadata = JsonSerializer.Deserialize<CacheEntryMetadata>(File.ReadAllBytes(metadataPath), SerializerOptions);
            if (metadata is null || metadata.Product is null || metadata.ProductVersion is null)
            {
                RemoveEntry(entryPath);
                return false;
            }

            byte[]? thumbnail = null;
            if (metadata.HasThumbnail)
            {
                if (!File.Exists(thumbnailPath))
                {
                    RemoveEntry(entryPath);
                    return false;
                }

                thumbnail = File.ReadAllBytes(thumbnailPath);
                if (thumbnail.Length == 0)
                {
                    RemoveEntry(entryPath);
                    return false;
                }
            }

            info = new CachedFamilyInfo
            {
                Product = metadata.Product,
                ProductVersion = metadata.ProductVersion,
                Updated = metadata.Updated,
                Thumbnail = thumbnail
            };

            return true;
        }
        catch (JsonException)
        {
            // Corrupted metadata (e.g. interrupted write): treat as a miss and self-heal.
            RemoveEntry(entryPath);
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Stores the information for the specified family file, replacing any stale entries of the same file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <param name="info">The information to store.</param>
    /// <exception cref="FileNotFoundException">Thrown when the family file does not exist.</exception>
    /// <remarks>
    ///     Writes are atomic per file: each part is written to a temporary file and then moved into
    ///     place. The metadata file is written after the thumbnail, so a concurrent reader never
    ///     observes a metadata file whose thumbnail is missing or incomplete. Entries belonging to
    ///     previous states of the same family file are deleted.
    /// </remarks>
    public void Store(string familyFilePath, CachedFamilyInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var fileInfo = new FileInfo(familyFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"The specified family file was not found: {familyFilePath}");
        }

        Directory.CreateDirectory(_cacheDirectory);

        var pathHash = HashText(fileInfo.FullName.ToUpperInvariant());
        var entryPath = GetEntryBasePath(fileInfo);

        // Remove entries of previous states of this family file before writing the current one.
        foreach (var stale in Directory.EnumerateFiles(_cacheDirectory, $"{pathHash}-*.*"))
        {
            if (!stale.StartsWith(entryPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(stale);
            }
        }

        if (info.Thumbnail is { Length: > 0 })
        {
            WriteAtomically(entryPath + ".png", info.Thumbnail);
        }

        var metadata = new CacheEntryMetadata
        {
            Product = info.Product,
            ProductVersion = info.ProductVersion,
            Updated = info.Updated,
            HasThumbnail = info.Thumbnail is { Length: > 0 }
        };

        WriteAtomically(entryPath + ".json", JsonSerializer.SerializeToUtf8Bytes(metadata, SerializerOptions));
    }

    /// <summary>
    ///     Computes the cache entry base path (without extension) for the current state of the family file.
    /// </summary>
    /// <param name="fileInfo">The family file.</param>
    /// <returns>The full path of the cache entry without extension.</returns>
    /// <remarks>
    ///     The entry name has the form <c>{pathHash}-{stateHash}</c>. The first component identifies the
    ///     family file independently of its content, which allows stale entries of the same file to be
    ///     located and removed. The second component changes whenever the file's last write time or size
    ///     changes, which implicitly invalidates the entry.
    /// </remarks>
    private string GetEntryBasePath(FileInfo fileInfo)
    {
        var pathHash = HashText(fileInfo.FullName.ToUpperInvariant());
        var stateHash = HashText($"{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}");

        return Path.Combine(_cacheDirectory, $"{pathHash}-{stateHash}");
    }

    private void RemoveEntry(string entryBasePath)
    {
        TryDelete(entryBasePath + ".json");
        TryDelete(entryBasePath + ".png");
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    private static string HashText(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        // 16 hex characters (64 bits) keep file names short while making collisions practically impossible.
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The entry is locked by a concurrent reader; it will be removed on a later store.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    ///     The persisted shape of the metadata part of a cache entry.
    /// </summary>
    private sealed record CacheEntryMetadata
    {
        public string? Product { get; init; }

        public string? ProductVersion { get; init; }

        public DateTime Updated { get; init; }

        public bool HasThumbnail { get; init; }
    }
}
