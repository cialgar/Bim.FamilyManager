using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Bim.FamilyManager.Rfa.Tests;

/// <summary>
///     Tests for <see cref="PartAtomReader" /> against real family files of different Revit versions
///     and categories (see the fixtures folder).
/// </summary>
public sealed class PartAtomReaderTests
{
    private readonly ITestOutputHelper _output;

    public PartAtomReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    }

    [Theory]
    [InlineData("furniture-coffee-table.rfa", "Furniture", "2025")]
    [InlineData("annotation-level-head.rfa", "Extremos iniciales de nivel", "2026")]
    [InlineData("generic-test.rfa", "Acopladores de armadura estructural", "2026")]
    // Note: the file name claims "v2025" but the family was actually saved with Revit 2026 —
    // the PartAtom product-version is the authoritative value.
    [InlineData("door-no-preview.rfa", "Doors", "2026")]
    public void Read_RealFamilyFile_ReturnsAuthoredCategoryAndVersion(string fixtureName, string expectedCategory, string expectedVersion)
    {
        // The category is stored in the language the family was authored in, which is why the
        // expected values mix English and Spanish across the fixtures.
        var info = PartAtomReader.Read(Fixture(fixtureName));

        Assert.NotNull(info);
        _output.WriteLine($"{fixtureName}: category='{info!.Category}', version='{info.ProductVersion}', " +
                          $"omniclass='{info.OmniClassNumber}', updated='{info.Updated:O}', symbols=[{string.Join(", ", info.SymbolNames)}]");

        Assert.Equal(expectedCategory, info.Category);
        Assert.Equal(expectedVersion, info.ProductVersion);
        Assert.NotNull(info.Updated);
        Assert.NotEmpty(info.SymbolNames);
    }

    [Fact]
    public void Read_ModelFamilyWithOmniClass_ReturnsOmniClassNumber()
    {
        var info = PartAtomReader.Read(Fixture("furniture-coffee-table.rfa"));

        Assert.Equal("23.40.20.00", info!.OmniClassNumber);
    }

    [Fact]
    public void Read_AnnotationFamily_HasNoOmniClassNumber()
    {
        var info = PartAtomReader.Read(Fixture("annotation-level-head.rfa"));

        Assert.Null(info!.OmniClassNumber);
    }

    [Fact]
    public void Read_TruncatedFile_ReturnsNull()
    {
        Assert.Null(PartAtomReader.Read(Fixture("corrupt.rfa")));
    }

    [Fact]
    public void Read_NotACompoundFile_ReturnsNull()
    {
        using var stream = new MemoryStream("This is not an OLE compound file."u8.ToArray());

        Assert.Null(PartAtomReader.Read(stream));
    }

    [Fact]
    public void Read_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        Assert.Null(PartAtomReader.Read(stream));
    }

    [Fact]
    public void Read_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => PartAtomReader.Read(Fixture("does-not-exist.rfa")));
    }
}
