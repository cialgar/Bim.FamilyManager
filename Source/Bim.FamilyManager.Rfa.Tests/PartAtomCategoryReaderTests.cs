using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Bim.FamilyManager.Rfa.Tests;

/// <summary>
///     Tests for <see cref="PartAtomCategoryReader" /> against real family files of different Revit
///     versions and categories (see the fixtures folder).
/// </summary>
public sealed class PartAtomCategoryReaderTests
{
    private readonly ITestOutputHelper _output;

    public PartAtomCategoryReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    }

    [Theory]
    [InlineData("furniture-coffee-table.rfa", "Furniture")]
    [InlineData("annotation-level-head.rfa", "Extremos iniciales de nivel")]
    [InlineData("generic-test.rfa", "Acopladores de armadura estructural")]
    [InlineData("door-no-preview.rfa", "Doors")]
    public void ReadCategory_RealFamilyFile_ReturnsAuthoredCategory(string fixtureName, string expectedCategory)
    {
        // The category is stored in the language the family was authored in, which is why the
        // expected values mix English and Spanish across the fixtures.
        var category = PartAtomCategoryReader.ReadCategory(Fixture(fixtureName));

        _output.WriteLine($"{fixtureName}: '{category}'");
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void ReadCategory_TruncatedFile_ReturnsNull()
    {
        Assert.Null(PartAtomCategoryReader.ReadCategory(Fixture("corrupt.rfa")));
    }

    [Fact]
    public void ReadCategory_NotACompoundFile_ReturnsNull()
    {
        using var stream = new MemoryStream("This is not an OLE compound file."u8.ToArray());

        Assert.Null(PartAtomCategoryReader.ReadCategory(stream));
    }

    [Fact]
    public void ReadCategory_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        Assert.Null(PartAtomCategoryReader.ReadCategory(stream));
    }

    [Fact]
    public void ReadCategory_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => PartAtomCategoryReader.ReadCategory(Fixture("does-not-exist.rfa")));
    }
}
