namespace Bim.FamilyManager.Ui;

/// <summary>
///     Provides access to the user-owned annotations of families: favorites and tags.
/// </summary>
/// <remarks>
///     Annotations are stored in the persistent family index and are addressed by family name — the
///     same identity the family manager uses for its in-session cache. When the same name exists in
///     several indexed sources, operations apply to all of them, which matches how the panel treats
///     same-named families as one entry. Family files are never modified.
/// </remarks>
public interface IFamilyAnnotations
{
    /// <summary>
    ///     Determines whether the family is marked as favorite.
    /// </summary>
    /// <param name="familyName">The family name.</param>
    /// <returns><c>true</c> if the family is a favorite; otherwise <c>false</c>.</returns>
    bool IsFavorite(string familyName);

    /// <summary>
    ///     Marks or unmarks the family as favorite.
    /// </summary>
    /// <param name="familyName">The family name.</param>
    /// <param name="isFavorite"><c>true</c> to mark as favorite; <c>false</c> to unmark.</param>
    void SetFavorite(string familyName, bool isFavorite);

    /// <summary>
    ///     Gets the tags assigned to the family, ordered alphabetically.
    /// </summary>
    /// <param name="familyName">The family name.</param>
    /// <returns>The tag names.</returns>
    IReadOnlyList<string> GetTags(string familyName);

    /// <summary>
    ///     Assigns a tag to the family, creating the tag on first use.
    /// </summary>
    /// <param name="familyName">The family name.</param>
    /// <param name="tag">The tag name.</param>
    void AddTag(string familyName, string tag);

    /// <summary>
    ///     Removes a tag from the family.
    /// </summary>
    /// <param name="familyName">The family name.</param>
    /// <param name="tag">The tag name.</param>
    void RemoveTag(string familyName, string tag);

    /// <summary>
    ///     Gets all tags present in the index, ordered alphabetically.
    /// </summary>
    /// <returns>The tag names.</returns>
    IReadOnlyList<string> GetAllTags();
}
