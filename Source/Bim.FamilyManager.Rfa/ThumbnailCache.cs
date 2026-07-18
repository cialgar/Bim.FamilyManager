using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Provides a persistent, per-file cache for family thumbnail images (PNG).
/// </summary>
/// <remarks>
///     Extracting a thumbnail requires opening the family file, which is expensive for large libraries.
///     This cache stores each extracted PNG on disk so that subsequent sessions can serve the thumbnail
///     without touching the family file at all.
///     Cache entries are keyed by the family file path and its last write time and size. When the family
///     file changes, the key changes as well, the stale entry is removed, and the thumbnail is extracted
///     again. The cache is self-healing: corrupted or externally deleted entries are treated as cache
///     misses and regenerated.
/// </remarks>
public sealed class ThumbnailCache
{
    private readonly string _cacheDirectory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThumbnailCache" /> class.
    /// </summary>
    /// <param name="cacheDirectory">The directory in which cached thumbnails are stored. Created on first write.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cacheDirectory" /> is null or whitespace.</exception>
    public ThumbnailCache(string cacheDirectory)
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
    /// <returns>A <see cref="ThumbnailCache" /> rooted at the default cache directory.</returns>
    public static ThumbnailCache CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new ThumbnailCache(Path.Combine(appData, "FamilyManager", "thumbnails"));
    }

    /// <summary>
    ///     Attempts to retrieve the cached thumbnail for the specified family file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <param name="thumbnail">The cached PNG bytes, or <c>null</c> when the method returns <c>false</c>.</param>
    /// <returns>
    ///     <c>true</c> if a cache entry exists for the current state (path, last write time, size) of the
    ///     family file; otherwise <c>false</c>. A missing family file, a missing entry, a stale entry or a
    ///     corrupted entry all result in <c>false</c>.
    /// </returns>
    public bool TryGetThumbnail(string familyFilePath, out byte[]? thumbnail)
    {
        thumbnail = null;

        var fileInfo = new FileInfo(familyFilePath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        var entryPath = GetEntryPath(fileInfo);

        try
        {
            if (!File.Exists(entryPath))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(entryPath);
            if (bytes.Length == 0)
            {
                // Corrupted entry (e.g. interrupted write): treat as a miss and self-heal.
                File.Delete(entryPath);
                return false;
            }

            thumbnail = bytes;
            return true;
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
    ///     Stores the thumbnail for the specified family file, replacing any stale entries of the same file.
    /// </summary>
    /// <param name="familyFilePath">The full path of the family file (*.rfa).</param>
    /// <param name="thumbnail">The PNG bytes to store.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="thumbnail" /> is empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the family file does not exist.</exception>
    /// <remarks>
    ///     The write is atomic: the entry is written to a temporary file and then moved into place, so a
    ///     concurrent reader never observes a partially written thumbnail. Entries belonging to previous
    ///     states of the same family file are deleted.
    /// </remarks>
    public void StoreThumbnail(string familyFilePath, byte[] thumbnail)
    {
        if (thumbnail is null || thumbnail.Length == 0)
        {
            throw new ArgumentException(@"Thumbnail cannot be null or empty.", nameof(thumbnail));
        }

        var fileInfo = new FileInfo(familyFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"The specified family file was not found: {familyFilePath}");
        }

        Directory.CreateDirectory(_cacheDirectory);

        var pathHash = HashText(fileInfo.FullName.ToUpperInvariant());
        var entryPath = GetEntryPath(fileInfo);

        // Remove entries of previous states of this family file before writing the current one.
        foreach (var stale in Directory.EnumerateFiles(_cacheDirectory, $"{pathHash}-*.png"))
        {
            if (!string.Equals(stale, entryPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(stale);
            }
        }

        var tempPath = entryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, thumbnail);
        File.Move(tempPath, entryPath, overwrite: true);
    }

    /// <summary>
    ///     Computes the cache entry file path for the current state of the specified family file.
    /// </summary>
    /// <param name="fileInfo">The family file.</param>
    /// <returns>The full path of the cache entry, encoding both the file identity and its state.</returns>
    /// <remarks>
    ///     The entry name has the form <c>{pathHash}-{stateHash}.png</c>. The first component identifies
    ///     the family file independently of its content, which allows stale entries of the same file to be
    ///     located and removed. The second component changes whenever the file's last write time or size
    ///     changes, which implicitly invalidates the entry.
    /// </remarks>
    private string GetEntryPath(FileInfo fileInfo)
    {
        var pathHash = HashText(fileInfo.FullName.ToUpperInvariant());
        var stateHash = HashText($"{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}");

        return Path.Combine(_cacheDirectory, $"{pathHash}-{stateHash}.png");
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
}
