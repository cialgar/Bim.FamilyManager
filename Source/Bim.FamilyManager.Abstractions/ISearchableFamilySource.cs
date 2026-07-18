namespace Bim.FamilyManager.Abstractions;

/// <summary>
///     Represents a family source that can answer search queries over its whole content without
///     enumerating the folder tree.
/// </summary>
/// <remarks>
///     Sources backed by a persistent index implement this interface to provide the global search of
///     the panel: results come from all folders of the source at once, combined with the filters of
///     <see cref="FamilySearchOptions" />. Sources that do not implement the interface keep the
///     folder-scoped search behavior.
///     This interface is additive — it does not change any existing contract of the abstractions.
/// </remarks>
public interface ISearchableFamilySource
{
    /// <summary>
    ///     Searches the source for families matching the specified text and filters.
    /// </summary>
    /// <param name="searchPattern">The search text, or <c>null</c> to match by filters only.</param>
    /// <param name="options">The filters to apply, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">A cancellation token to observe while searching.</param>
    /// <returns>An asynchronous stream of matching families.</returns>
    IAsyncEnumerable<IRevitFamily> SearchFamiliesAsync(string? searchPattern, FamilySearchOptions? options,
                                                       CancellationToken cancellationToken);
}

/// <summary>
///     Represents the combinable filters of a global family search.
/// </summary>
public sealed class FamilySearchOptions
{
    /// <summary>Gets the localized category to filter by, or <c>null</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the language-independent category key to filter by, or <c>null</c>.</summary>
    public string? CategoryKey { get; init; }

    /// <summary>Gets the product version to filter by, or <c>null</c>.</summary>
    public string? ProductVersion { get; init; }

    /// <summary>Gets the tag to filter by, or <c>null</c>.</summary>
    public string? Tag { get; init; }

    /// <summary>Gets a value indicating whether only favorite families are returned.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>
    ///     Gets a value indicating whether any filter is set.
    /// </summary>
    public bool HasAnyFilter => Category is not null || CategoryKey is not null || ProductVersion is not null ||
                                Tag is not null || FavoritesOnly;
}
