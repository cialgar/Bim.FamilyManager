using System.IO;
using Xunit;

namespace Bim.FamilyManager.Rfa.Tests;

/// <summary>
///     Tests for <see cref="FamilyInfoCache" /> covering hits, misses, invalidation and self-healing.
/// </summary>
public sealed class FamilyInfoCacheTests : IDisposable
{
    private static readonly byte[] SamplePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];
    private static readonly byte[] OtherPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x09, 0x08];

    private readonly string _root;
    private readonly string _cacheDirectory;
    private readonly string _familyFile;

    public FamilyInfoCacheTests()
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

    private static CachedFamilyInfo CreateInfo(byte[]? thumbnail = null)
    {
        return new CachedFamilyInfo
        {
            Product = "Revit",
            ProductVersion = "2026",
            Updated = new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Utc),
            Thumbnail = thumbnail
        };
    }

    [Fact]
    public void TryGet_WithoutStoredEntry_ReturnsFalse()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);

        Assert.False(cache.TryGet(_familyFile, out var info));
        Assert.Null(info);
    }

    [Fact]
    public void TryGet_AfterStore_ReturnsStoredInfo()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);

        cache.Store(_familyFile, CreateInfo(SamplePng));

        Assert.True(cache.TryGet(_familyFile, out var info));
        Assert.Equal("Revit", info!.Product);
        Assert.Equal("2026", info.ProductVersion);
        Assert.Equal(new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Utc), info.Updated);
        Assert.Equal(SamplePng, info.Thumbnail);
    }

    [Fact]
    public void TryGet_EntryWithoutThumbnail_ReturnsInfoWithNullThumbnail()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);

        cache.Store(_familyFile, CreateInfo());

        Assert.True(cache.TryGet(_familyFile, out var info));
        Assert.Null(info!.Thumbnail);
    }

    [Fact]
    public void TryGet_SurvivesNewCacheInstance()
    {
        new FamilyInfoCache(_cacheDirectory).Store(_familyFile, CreateInfo(SamplePng));

        Assert.True(new FamilyInfoCache(_cacheDirectory).TryGet(_familyFile, out var info));
        Assert.Equal(SamplePng, info!.Thumbnail);
    }

    [Fact]
    public void TryGet_AfterFamilyFileChanged_ReturnsFalse()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        // Change both content size and last write time.
        File.WriteAllBytes(_familyFile, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.False(cache.TryGet(_familyFile, out _));
    }

    [Fact]
    public void Store_AfterFamilyFileChanged_RemovesStaleEntries()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        File.WriteAllBytes(_familyFile, [1, 2, 3, 4, 5, 6, 7, 8]);
        cache.Store(_familyFile, CreateInfo(OtherPng));

        // Exactly one entry (json + png) remains, and it is the current one.
        Assert.Single(Directory.GetFiles(_cacheDirectory, "*.json"));
        Assert.Single(Directory.GetFiles(_cacheDirectory, "*.png"));
        Assert.True(cache.TryGet(_familyFile, out var info));
        Assert.Equal(OtherPng, info!.Thumbnail);
    }

    [Fact]
    public void TryGet_CorruptedMetadata_IsTreatedAsMissAndRemoved()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        var metadata = Directory.GetFiles(_cacheDirectory, "*.json").Single();
        File.WriteAllText(metadata, "{ not valid json");

        Assert.False(cache.TryGet(_familyFile, out _));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.json"));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.png"));
    }

    [Fact]
    public void TryGet_MissingThumbnailForEntryThatHasOne_IsTreatedAsMissAndRemoved()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        File.Delete(Directory.GetFiles(_cacheDirectory, "*.png").Single());

        Assert.False(cache.TryGet(_familyFile, out _));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.json"));
    }

    [Fact]
    public void TryGet_EmptyThumbnailFile_IsTreatedAsMissAndRemoved()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        File.WriteAllBytes(Directory.GetFiles(_cacheDirectory, "*.png").Single(), []);

        Assert.False(cache.TryGet(_familyFile, out _));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.json"));
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.png"));
    }

    [Fact]
    public void TryGet_MissingFamilyFile_ReturnsFalse()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);

        Assert.False(cache.TryGet(Path.Combine(_root, "does-not-exist.rfa"), out _));
    }

    [Fact]
    public void TryGet_CacheDirectoryDeletedExternally_ReturnsFalseAndHealsOnNextStore()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);
        cache.Store(_familyFile, CreateInfo(SamplePng));

        Directory.Delete(_cacheDirectory, recursive: true);

        Assert.False(cache.TryGet(_familyFile, out _));

        cache.Store(_familyFile, CreateInfo(SamplePng));
        Assert.True(cache.TryGet(_familyFile, out _));
    }

    [Fact]
    public void Store_MissingFamilyFile_Throws()
    {
        var cache = new FamilyInfoCache(_cacheDirectory);

        Assert.Throws<FileNotFoundException>(() => cache.Store(Path.Combine(_root, "missing.rfa"), CreateInfo()));
    }
}
