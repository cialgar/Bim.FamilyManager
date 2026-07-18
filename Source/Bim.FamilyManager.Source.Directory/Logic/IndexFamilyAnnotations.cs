using Bim.FamilyManager.Index;
using Bim.FamilyManager.Ui;

namespace Bim.FamilyManager.Source.Directory.Logic;

/// <summary>
///     Implements <see cref="IFamilyAnnotations" /> on top of the persistent family index.
/// </summary>
/// <remarks>
///     Family names are resolved to index records via <see cref="IndexQuery.FindByName" />; write
///     operations apply to every record with the name (one per indexed source). Families that are not
///     indexed (e.g. served by a classic <see cref="DirectorySource" /> without an indexed sibling)
///     have no records, so reads return empty state and writes are no-ops.
/// </remarks>
public sealed class IndexFamilyAnnotations : IFamilyAnnotations
{
    private readonly IndexEditor _editor;
    private readonly IndexQuery _query;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndexFamilyAnnotations" /> class.
    /// </summary>
    /// <param name="familyIndex">The persistent family index.</param>
    public IndexFamilyAnnotations(FamilyIndex familyIndex)
    {
        _query = new IndexQuery(familyIndex);
        _editor = new IndexEditor(familyIndex);
    }

    /// <inheritdoc />
    public bool IsFavorite(string familyName)
    {
        return _query.FindByName(familyName).Any(record => _editor.IsFavorite(record.Id));
    }

    /// <inheritdoc />
    public void SetFavorite(string familyName, bool isFavorite)
    {
        foreach (var record in _query.FindByName(familyName))
        {
            _editor.SetFavorite(record.Id, isFavorite);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetTags(string familyName)
    {
        return _query.FindByName(familyName)
                     .SelectMany(record => _editor.GetTags(record.Id))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    /// <inheritdoc />
    public void AddTag(string familyName, string tag)
    {
        foreach (var record in _query.FindByName(familyName))
        {
            _editor.AddTag(record.Id, tag);
        }
    }

    /// <inheritdoc />
    public void RemoveTag(string familyName, string tag)
    {
        foreach (var record in _query.FindByName(familyName))
        {
            _editor.RemoveTag(record.Id, tag);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllTags()
    {
        return _editor.GetAllTags();
    }
}
