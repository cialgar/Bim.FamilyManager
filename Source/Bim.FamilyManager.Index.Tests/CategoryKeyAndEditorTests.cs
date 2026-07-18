using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Bim.FamilyManager.Index.Tests;

/// <summary>
///     Tests for <see cref="CategoryKeyMap" />, the category-key filling of the scanner, and the
///     favorites/tags editing of <see cref="IndexEditor" />.
/// </summary>
public sealed class CategoryKeyAndEditorTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceRoot;
    private readonly FamilyIndex _index;
    private readonly IndexScanner _scanner;
    private readonly IndexQuery _query;
    private readonly IndexEditor _editor;

    public CategoryKeyAndEditorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FamilyManagerTests", Guid.NewGuid().ToString("N"));
        _sourceRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(_sourceRoot);

        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        File.Copy(Path.Combine(fixtures, "furniture-coffee-table.rfa"), Path.Combine(_sourceRoot, "Coffee table2.rfa"));
        File.Copy(Path.Combine(fixtures, "annotation-level-head.rfa"), Path.Combine(_sourceRoot, "Level Head.rfa"));
        File.Copy(Path.Combine(fixtures, "generic-test.rfa"), Path.Combine(_sourceRoot, "Rebar coupler.rfa"));
        File.Copy(Path.Combine(fixtures, "door-no-preview.rfa"), Path.Combine(_sourceRoot, "Groove door.rfa"));

        _index = new FamilyIndex(Path.Combine(_root, "index.db"));
        _scanner = new IndexScanner(_index);
        _query = new IndexQuery(_index);
        _editor = new IndexEditor(_index);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Furniture", "furniture")]
    [InlineData("Mobiliario", "furniture")]
    [InlineData("Doors", "doors")]
    [InlineData("Puertas", "doors")]
    [InlineData("Extremos iniciales de nivel", "level-heads")]
    [InlineData("Acopladores de armadura estructural", "structural-rebar-couplers")]
    [InlineData("  mobiliario  ", "furniture")]
    public void TryGetKey_KnownLocalizations_ShareTheSameKey(string term, string expectedKey)
    {
        Assert.True(CategoryKeyMap.TryGetKey(term, out var key));
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("Categoría inventada")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetKey_UnknownOrEmptyTerm_ReturnsFalse(string? term)
    {
        Assert.False(CategoryKeyMap.TryGetKey(term, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void Scan_FillsCategoryKeyAcrossLanguages()
    {
        _scanner.Scan(_sourceRoot);

        var coffee = _query.Search("coffee").Single();
        var coupler = _query.Search("rebar").Single();
        var door = _query.Search("groove").Single();

        Assert.Equal("furniture", coffee.CategoryKey);
        Assert.Equal("structural-rebar-couplers", coupler.CategoryKey);
        Assert.Equal("doors", door.CategoryKey);
    }

    [Fact]
    public void Search_FilterByCategoryKey_UnifiesLocalizedVariants()
    {
        _scanner.Scan(_sourceRoot);

        // "Furniture" (English fixture) is found via the language-independent key.
        var results = _query.Search(null, new SearchFilter { CategoryKey = "furniture" });

        Assert.Single(results);
        Assert.Equal("Furniture", results[0].Category);
    }

    [Fact]
    public void Scan_BackfillsCategoryKeyForRowsIndexedBeforeTheMapKnewThem()
    {
        _scanner.Scan(_sourceRoot);

        // Simulate rows indexed by an older version without the map: null the keys directly.
        using (var connection = new SqliteConnection($"Data Source={_index.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE families SET category_key = NULL;";
            command.ExecuteNonQuery();
        }

        // An incremental scan with no file changes must restore the keys without re-reading files.
        var rescan = _scanner.Scan(_sourceRoot);

        Assert.Equal(4, rescan.Unchanged);
        Assert.Equal("furniture", _query.Search("coffee").Single().CategoryKey);
    }

    [Fact]
    public void Favorites_RoundtripAndFilter_PersistAcrossInstances()
    {
        _scanner.Scan(_sourceRoot);
        var coffee = _query.Search("coffee").Single();

        _editor.SetFavorite(coffee.Id, true);

        Assert.True(new IndexEditor(_index).IsFavorite(coffee.Id));

        var favorites = new IndexQuery(_index).Search(null, new SearchFilter { FavoritesOnly = true });
        Assert.Single(favorites);
        Assert.Equal(coffee.Id, favorites[0].Id);

        _editor.SetFavorite(coffee.Id, false);
        Assert.False(_editor.IsFavorite(coffee.Id));
        Assert.Empty(_query.Search(null, new SearchFilter { FavoritesOnly = true }));
    }

    [Fact]
    public void Tags_RoundtripAndFilter_PersistAcrossInstances()
    {
        _scanner.Scan(_sourceRoot);
        var coffee = _query.Search("coffee").Single();
        var door = _query.Search("groove").Single();

        _editor.AddTag(coffee.Id, "exterior");
        _editor.AddTag(coffee.Id, "premium");
        _editor.AddTag(door.Id, "premium");

        Assert.Equal(["exterior", "premium"], new IndexEditor(_index).GetTags(coffee.Id));
        Assert.Equal(["exterior", "premium"], _editor.GetAllTags());

        var premium = _query.Search(null, new SearchFilter { Tag = "premium" });
        Assert.Equal(2, premium.Count);

        _editor.RemoveTag(coffee.Id, "exterior");
        Assert.Equal(["premium"], _editor.GetTags(coffee.Id));

        // "exterior" is no longer used by any family and must be pruned from the tag list.
        Assert.Equal(["premium"], _editor.GetAllTags());
    }

    [Fact]
    public void Tags_DeletingFamilyFile_RemovesItsAnnotations()
    {
        _scanner.Scan(_sourceRoot);
        var coffee = _query.Search("coffee").Single();
        _editor.AddTag(coffee.Id, "premium");
        _editor.SetFavorite(coffee.Id, true);

        File.Delete(Path.Combine(_sourceRoot, "Coffee table2.rfa"));
        _scanner.Scan(_sourceRoot);

        Assert.False(_editor.IsFavorite(coffee.Id));
        Assert.Empty(_editor.GetTags(coffee.Id));
    }
}
