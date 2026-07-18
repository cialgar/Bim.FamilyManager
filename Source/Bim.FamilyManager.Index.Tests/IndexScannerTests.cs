using System.IO;
using Xunit;

namespace Bim.FamilyManager.Index.Tests;

/// <summary>
///     Tests for <see cref="IndexScanner" /> and <see cref="IndexQuery" /> against a temporary source
///     directory populated with the real .rfa fixtures.
/// </summary>
public sealed class IndexScannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceRoot;
    private readonly FamilyIndex _index;
    private readonly IndexScanner _scanner;
    private readonly IndexQuery _query;

    public IndexScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FamilyManagerTests", Guid.NewGuid().ToString("N"));
        _sourceRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(Path.Combine(_sourceRoot, "furniture"));
        Directory.CreateDirectory(Path.Combine(_sourceRoot, "annotations"));

        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        File.Copy(Path.Combine(fixtures, "furniture-coffee-table.rfa"), Path.Combine(_sourceRoot, "furniture", "Coffee table2.rfa"));
        File.Copy(Path.Combine(fixtures, "door-no-preview.rfa"), Path.Combine(_sourceRoot, "furniture", "Groove door.rfa"));
        File.Copy(Path.Combine(fixtures, "annotation-level-head.rfa"), Path.Combine(_sourceRoot, "annotations", "Level Head.rfa"));
        File.Copy(Path.Combine(fixtures, "generic-test.rfa"), Path.Combine(_sourceRoot, "Rebar coupler.rfa"));
        File.Copy(Path.Combine(fixtures, "corrupt.rfa"), Path.Combine(_sourceRoot, "Broken.rfa"));

        // A Revit backup file: must be ignored by the scanner.
        File.Copy(Path.Combine(fixtures, "generic-test.rfa"), Path.Combine(_sourceRoot, "Rebar coupler.0001.rfa"));

        _index = new FamilyIndex(Path.Combine(_root, "index.db"));
        _scanner = new IndexScanner(_index);
        _query = new IndexQuery(_index);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_FreshSource_IndexesAllFamiliesExceptBackups()
    {
        var result = _scanner.Scan(_sourceRoot);

        Assert.Equal(5, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Removed);
        Assert.Equal(0, result.Unchanged);
        Assert.Single(result.FailedExtractions);
        Assert.EndsWith("Broken.rfa", result.FailedExtractions[0]);
    }

    [Fact]
    public void Scan_SecondRunWithoutChanges_ReadsNothing()
    {
        _scanner.Scan(_sourceRoot);
        var result = _scanner.Scan(_sourceRoot);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Removed);
        Assert.Equal(5, result.Unchanged);
        Assert.Empty(result.FailedExtractions);
    }

    [Fact]
    public void Scan_ModifiedFile_IsReindexed()
    {
        _scanner.Scan(_sourceRoot);

        var target = Path.Combine(_sourceRoot, "furniture", "Coffee table2.rfa");
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(5));

        var result = _scanner.Scan(_sourceRoot);

        Assert.Equal(1, result.Updated);
        Assert.Equal(4, result.Unchanged);
    }

    [Fact]
    public void Scan_DeletedFile_IsRemoved()
    {
        _scanner.Scan(_sourceRoot);
        File.Delete(Path.Combine(_sourceRoot, "annotations", "Level Head.rfa"));

        var result = _scanner.Scan(_sourceRoot);

        Assert.Equal(1, result.Removed);
        Assert.Empty(_query.Search("level"));
    }

    [Fact]
    public void Scan_ReportsProgressForEachFile()
    {
        var reports = new List<ScanProgress>();

        _scanner.Scan(_sourceRoot, new SynchronousProgress(reports));

        Assert.Equal(5, reports.Count);
        Assert.Equal(5, reports[^1].Total);
    }

    [Fact]
    public void Search_ByNamePrefix_FindsFamily()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search("cof");

        Assert.Single(results);
        Assert.Equal("Coffee table2", results[0].Name);
        Assert.Equal("Furniture", results[0].Category);
        Assert.Equal("23.40.20.00", results[0].OmniClassNumber);
        Assert.Equal("furniture", results[0].Folder);
    }

    [Fact]
    public void Search_BySymbolName_FindsFamily()
    {
        _scanner.Scan(_sourceRoot);

        // "96" only appears in the door's type names (e.g. 36" x 96"), not in any family name.
        var results = _query.Search("96");

        Assert.Single(results);
        Assert.Equal("Groove door", results[0].Name);
    }

    [Fact]
    public void Search_CorruptFileIsStillFindableByName()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search("broken");

        Assert.Single(results);
        Assert.Null(results[0].Category);
        Assert.Null(results[0].ProductVersion);
    }

    [Fact]
    public void Search_FilterByCategory_ReturnsOnlyMatching()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search(null, new SearchFilter { Category = "Furniture" });

        Assert.Single(results);
        Assert.Equal("Coffee table2", results[0].Name);
    }

    [Fact]
    public void Search_FilterByProductVersion_ReturnsOnlyMatching()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search(null, new SearchFilter { ProductVersion = "2026" });

        // Level head, rebar coupler and the groove door (saved with 2026 despite its "v2025" file name).
        Assert.Equal(3, results.Count);
        Assert.All(results, record => Assert.Equal("2026", record.ProductVersion));
    }

    [Fact]
    public void Search_FilterByFolder_IncludesSubfoldersOnly()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search(null, new SearchFilter { FolderPrefix = "furniture" });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void GetCategories_ReturnsDistinctSortedCategories()
    {
        _scanner.Scan(_sourceRoot);

        var categories = _query.GetCategories();

        Assert.Equal(4, categories.Count);
        Assert.Contains("Furniture", categories);
        Assert.Contains("Doors", categories);
    }

    [Fact]
    public void Search_CategoryKeyIsNullUntilPhase3Normalization()
    {
        _scanner.Scan(_sourceRoot);

        var results = _query.Search("coffee");

        Assert.Null(results[0].CategoryKey);
    }

    /// <summary>
    ///     A progress sink that records reports synchronously (Progress&lt;T&gt; posts to a sync context,
    ///     which xUnit does not pump reliably).
    /// </summary>
    private sealed class SynchronousProgress(List<ScanProgress> reports) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value)
        {
            reports.Add(value);
        }
    }
}
