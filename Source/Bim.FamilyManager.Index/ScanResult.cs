namespace Bim.FamilyManager.Index;

/// <summary>
///     Represents the outcome of an incremental scan performed by <see cref="IndexScanner" />.
/// </summary>
public sealed record ScanResult
{
    /// <summary>Gets the number of families added to the index.</summary>
    public required int Added { get; init; }

    /// <summary>Gets the number of families whose files changed and were re-extracted.</summary>
    public required int Updated { get; init; }

    /// <summary>Gets the number of families removed from the index because their files no longer exist.</summary>
    public required int Removed { get; init; }

    /// <summary>Gets the number of families whose files were unchanged (no extraction performed).</summary>
    public required int Unchanged { get; init; }

    /// <summary>
    ///     Gets the files that were indexed with basic information only because their metadata could not
    ///     be extracted (corrupted or unreadable PartAtom stream).
    /// </summary>
    public required IReadOnlyList<string> FailedExtractions { get; init; }

    /// <summary>Gets the total scan duration.</summary>
    public required TimeSpan Duration { get; init; }
}

/// <summary>
///     Represents the progress of a scan, reported per processed file.
/// </summary>
/// <param name="Processed">The number of files processed so far.</param>
/// <param name="Total">The total number of files to process.</param>
/// <param name="CurrentFile">The full path of the file currently being processed.</param>
public sealed record ScanProgress(int Processed, int Total, string CurrentFile);
