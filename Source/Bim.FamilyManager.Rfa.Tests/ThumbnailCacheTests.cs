using System.IO;
using Xunit;

namespace Bim.FamilyManager.Rfa.Tests;

/// <summary>
///     Tests for <see cref="ThumbnailCache" /> covering hits, misses, invalidation and self-healing.
/// </summary>
public sealed class ThumbnailCacheTests : IDisposable
{
    private static readonly byte[] SamplePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];
    private static readonly byte[] OtherPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x09, 0x08];

    private readonly string _root;
    private readonly string _cacheDirectory;
    private readonly string _familyFile;

    public ThumbnailCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FamilyManagerTests", Guid.NewGuid().ToString("N"));
        _cacheDirectory = Path.Combine(_root, "thumbnails");
        _familyFile = Path.Combine(_root, "family.rfa");

        Directory.CreateDirectory(_root);
        File.WriteAllBytes(_familyFile, [1, 2, 3, 4, 5]);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TryGetThumbnail_WithoutStoredEntry_ReturnsFalse()
    {
        var cache = new ThumbnailCache(_cacheDirectory);

        Assert.False(cache.TryGetThumbnail(_familyFile, out var thumbnail));
        Assert.Null(thumbnail);
    }

    [Fact]
    public void TryGetThumbnail_AfterStore_ReturnsStoredBytes()
    {
        var cache = new ThumbnailCache(_cacheDirectory);

        cache.StoreThumbnail(_familyFile, SamplePng);

        Assert.True(cache.TryGetThumbnail(_familyFile, out var thumbnail));
        Assert.Equal(SamplePng, thumbnail);
    }

    [Fact]
    public void TryGetThumbnail_SurvivesNewCacheInstance()
    {
        new ThumbnailCache(_cacheDirectory).StoreThumbnail(_familyFile, SamplePng);

        Assert.True(new ThumbnailCache(_cacheDirectory).TryGetThumbnail(_familyFile, out var thumbnail));
        Assert.Equal(SamplePng, thumbnail);
    }

    [Fact]
    public void TryGetThumbnail_AfterFamilyFileChanged_ReturnsFalse()
    {
        var cache = new ThumbnailCache(_cacheDirectory);
        cache.StoreThumbnail(_familyFile, SamplePng);

        // Change both content size and last write time.
        File.WriteAllBytes(_familyFile, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.False(cache.TryGetThumbnail(_familyFile, out _));
    }

    [Fact]
    public void StoreThumbnail_AfterFamilyFileChanged_RemovesStaleEntry()
    {
        var cache = new ThumbnailCache(_cacheDirectory);
        cache.StoreThumbnail(_familyFile, SamplePng);

        File.WriteAllBytes(_familyFile, [1, 2, 3, 4, 5, 6, 7, 8]);
        cache.StoreThumbnail(_familyFile, OtherPng);

        // Exactly one entry remains for the family file, and it is the current one.
        Assert.Single(Directory.GetFiles(_cacheDirectory, "*.png"));
        Assert.True(cache.TryGetThumbnail(_familyFile, out var thumbnail));
        Assert.Equal(OtherPng, thumbnail);
    }

    [Fact]
    public void TryGetThumbnail_CorruptedEmptyEntry_IsTreatedAsMissAndRemoved()
    {
        var cache = new ThumbnailCache(_cacheDirectory);
        cache.StoreThumbnail(_familyFile, SamplePng);

        // Corrupt the entry: truncate to zero bytes (simulates an interrupted write).
        var entry = Directory.GetFiles(_cacheDirectory, "*.png").Single();
        File.WriteAllBytes(entry, []);

        Assert.False(cache.TryGetThumbnail(_familyFile, out _));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.png"));
    }

    [Fact]
    public void TryGetThumbnail_MissingFamilyFile_ReturnsFalse()
    {
        var cache = new ThumbnailCache(_cacheDirectory);

        Assert.False(cache.TryGetThumbnail(Path.Combine(_root, "does-not-exist.rfa"), out _));
    }

    [Fact]
    public void TryGetThumbnail_CacheDirectoryDeletedExternally_ReturnsFalse()
    {
        var cache = new ThumbnailCache(_cacheDirectory);
        cache.StoreThumbnail(_familyFile, SamplePng);

        Directory.Delete(_cacheDirectory, recursive: true);

        Assert.False(cache.TryGetThumbnail(_familyFile, out _));

        // And the cache heals on the next store.
        cache.StoreThumbnail(_familyFile, SamplePng);
        Assert.True(cache.TryGetThumbnail(_familyFile, out _));
    }

    [Fact]
    public void StoreThumbnail_EmptyThumbnail_Throws()
    {
        var cache = new ThumbnailCache(_cacheDirectory);

        Assert.Throws<ArgumentException>(() => cache.StoreThumbnail(_familyFile, []));
    }

    [Fact]
    public void StoreThumbnail_MissingFamilyFile_Throws()
    {
        var cache = new ThumbnailCache(_cacheDirectory);

        Assert.Throws<FileNotFoundException>(() => cache.StoreThumbnail(Path.Combine(_root, "missing.rfa"), SamplePng));
    }
}
