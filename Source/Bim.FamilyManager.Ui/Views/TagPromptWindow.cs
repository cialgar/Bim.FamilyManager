using System.Windows;
using System.Windows.Controls;

namespace Bim.FamilyManager.Ui.Views;

/// <summary>
///     A minimal modal prompt for entering a new tag name.
/// </summary>
/// <remarks>
///     Built in code because it is a trivial input dialog; a XAML view would add markup-compile
///     surface for no benefit.
/// </remarks>
public sealed class TagPromptWindow : Window
{
    private readonly TextBox _textBox;

    private TagPromptWindow()
    {
        Title = "New tag";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;

        _textBox = new TextBox { Margin = new Thickness(10, 10, 10, 0) };

        var okButton = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 10, 10, 10), IsDefault = true };
        okButton.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var cancelButton = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(0, 10, 10, 10), IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel();
        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => _textBox.Focus();
    }

    /// <summary>
    ///     Shows the prompt and returns the entered tag name.
    /// </summary>
    /// <returns>The entered text, or <c>null</c> if the user cancelled.</returns>
    public static string? Prompt()
    {
        var window = new TagPromptWindow();
        return window.ShowDialog() == true ? window._textBox.Text : null;
    }
}
