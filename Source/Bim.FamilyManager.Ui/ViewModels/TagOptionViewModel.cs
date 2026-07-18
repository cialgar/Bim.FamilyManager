namespace Bim.FamilyManager.Ui.ViewModels;

/// <summary>
///     Represents one tag entry of the family card context menu: the tag name and whether it is
///     currently assigned to the family.
/// </summary>
/// <param name="Name">The tag name.</param>
/// <param name="IsAssigned"><c>true</c> when the tag is assigned to the family.</param>
public sealed record TagOptionViewModel(string Name, bool IsAssigned);
