namespace Bim.FamilyManager.Rfa;

/// <summary>
///     Represents the cached display information of a family file: the metadata shown on the family
///     cards plus the native thumbnail.
/// </summary>
/// <remarks>
///     This is exactly the information the panel needs to render a family card without opening the
///     family file. Family symbols are intentionally not cached: they are only needed on interaction
///     (tooltips, drag &amp; drop), at which point the family file is read anyway.
/// </remarks>
public sealed record CachedFamilyInfo
{
    /// <summary>
    ///     Gets the product name the family was saved with (e.g. "Revit").
    /// </summary>
    public required string Product { get; init; }

    /// <summary>
    ///     Gets the product version the family was saved with (e.g. "2026").
    /// </summary>
    public required string ProductVersion { get; init; }

    /// <summary>
    ///     Gets the date and time the family was last updated, as stored in the family file.
    /// </summary>
    public required DateTime Updated { get; init; }

    /// <summary>
    ///     Gets the native thumbnail (PNG), or <c>null</c> when the family file has no preview image.
    /// </summary>
    public byte[]? Thumbnail { get; init; }
}
