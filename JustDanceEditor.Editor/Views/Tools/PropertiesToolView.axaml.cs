using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using JustDanceEditor.Editor.ViewModels.Tools;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class PropertiesToolView : UserControl
{
    public PropertiesToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox tb)
        {
            if (e.Key == Key.Enter)
            {
                // Commit changes by moving focus, triggering LostFocus update
                // Try focusing the parent or the UserControl itself.
                // Note: The UserControl must have Focusable="True" in XAML for this to work effectively.
                var topLevel = TopLevel.GetTopLevel(this);
                topLevel?.FocusManager?.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Cancel changes
                if (tb.DataContext is PropertyItemViewModel vm)
                {
                    // Revert text to current ViewModel value
                    tb.Text = vm.StringValue;
                    
                    // Move focus to cancel edit mode
                    this.Focus();
                }
                e.Handled = true;
            }
        }
    }
}
