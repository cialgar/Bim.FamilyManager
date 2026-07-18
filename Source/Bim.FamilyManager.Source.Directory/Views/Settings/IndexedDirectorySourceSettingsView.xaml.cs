using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Bim.FamilyManager.Source.Directory.Views.Settings;

/// <summary>
///     Represents the view for configuring the settings of an index-backed directory family source.
/// </summary>
/// <remarks>
///     This class is a WPF UserControl associated with the
///     <see cref="ViewModels.Settings.IndexedDirectorySourceSettingsViewModel" /> through the MVVM pattern.
/// </remarks>
public partial class IndexedDirectorySourceSettingsView : UserControl
{
    public IndexedDirectorySourceSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Handles the folder selection process triggered by a user interaction.
    /// </summary>
    /// <param name="sender">The source of the event, typically the control that was clicked.</param>
    /// <param name="e">The event data associated with the folder selection action.</param>
    private void OnSelectFolder(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Select family directory",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (folderDialog.ShowDialog() == true)
        {
            FolderTextBox.Text = folderDialog.FolderName;
        }
    }
}
