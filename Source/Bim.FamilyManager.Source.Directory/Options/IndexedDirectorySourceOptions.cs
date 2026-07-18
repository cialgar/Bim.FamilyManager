using Bim.FamilyManager.Base.Options;
using Bim.FamilyManager.Source.Directory.Logic;

namespace Bim.FamilyManager.Source.Directory.Options;

/// <summary>
///     Represents the configuration options for an index-backed directory family source.
/// </summary>
/// <remarks>
///     This class extends <see cref="FamilySourceOptions" /> for the
///     <see cref="IndexedDirectorySource" />, which browses the persistent SQLite family index instead
///     of scanning the directory tree on every session. Intended for large libraries.
/// </remarks>
[FamilySourceOptions(OptionsName = "IndexedDirectorySource")]
public class IndexedDirectorySourceOptions : FamilySourceOptions
{
    /// <summary>
    ///     Gets or sets the file system path to the directory containing the Revit families.
    /// </summary>
    /// <remarks>
    ///     This property specifies the root directory whose family files are indexed and browsed by the
    ///     <see cref="IndexedDirectorySource" />.
    /// </remarks>
    public string Path { get; set; } = string.Empty;
}
