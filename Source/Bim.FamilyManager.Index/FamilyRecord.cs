namespace Bim.FamilyManager.Index;

/// <summary>
///     Represents an indexed family file as returned by <see cref="IndexQuery" />.
/// </summary>
public sealed record FamilyRecord
{
    /// <summary>Gets the database identifier of the family.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the full path of the family file.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the family name (file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the folder of the family relative to the source root (empty string for the root itself).</summary>
    public required string Folder { get; init; }

    /// <summary>Gets the file size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>Gets the product version the family was saved with, or <c>null</c> if unknown.</summary>
    public string? ProductVersion { get; init; }

    /// <summary>Gets the localized family category as authored, or <c>null</c> if unknown.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the language-independent category key, or <c>null</c> until the phase 3 normalization map fills it.</summary>
    public string? CategoryKey { get; init; }

    /// <summary>Gets the OmniClass number, or <c>null</c> when the family carries none.</summary>
    public string? OmniClassNumber { get; init; }

    /// <summary>Gets the date and time the family was last updated (UTC), or <c>null</c> if unknown.</summary>
    public DateTime? Updated { get; init; }
}
