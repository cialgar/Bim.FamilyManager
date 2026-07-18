namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Represents the metadata stored in the <c>PartAtom</c> stream of a Revit family file.
/// </summary>
/// <remarks>
///     All values are read as authored: the category term is localized to the language the family was
///     created in, and the OmniClass number is only present for model families that carry an OmniClass
///     classification (annotation families do not).
/// </remarks>
public sealed record PartAtomInfo
{
    /// <summary>
    ///     Gets the family title (usually the family name without extension), or <c>null</c> if absent.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    ///     Gets the product version the family was saved with (e.g. "2026"), or <c>null</c> if absent.
    /// </summary>
    public string? ProductVersion { get; init; }

    /// <summary>
    ///     Gets the date and time the family was last updated (UTC), or <c>null</c> if absent.
    /// </summary>
    public DateTime? Updated { get; init; }

    /// <summary>
    ///     Gets the family category as authored (scheme <c>adsk:revit:grouping</c>, localized), or <c>null</c> if absent.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    ///     Gets the OmniClass number (scheme <c>std:oc1</c>), or <c>null</c> when the family carries none.
    /// </summary>
    public string? OmniClassNumber { get; init; }

    /// <summary>
    ///     Gets the names of the family types (symbols) defined in the family.
    /// </summary>
    public required IReadOnlyList<string> SymbolNames { get; init; }
}
