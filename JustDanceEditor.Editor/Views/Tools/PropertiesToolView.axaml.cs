using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using JustDanceEditor.Editor.ViewModels.Tools;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class PropertiesToolView : UserControl
{
    private PropertyItemViewModel? _lastColorPickerViewModel;

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

    private void OnColorPickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Find the PropertyItemViewModel for this ColorPicker
        if (sender is ColorPicker colorPicker)
        {
            var vm = colorPicker.DataContext as PropertyItemViewModel;
            if (vm == null)
                return;

            // Check if this is a new color picker (different from the last one)
            if (vm != _lastColorPickerViewModel)
            {
                // Close the previous color picker if there was one
                if (_lastColorPickerViewModel != null)
                {
                    _lastColorPickerViewModel.OnColorPickerClosed();
                }

                // Open the new one
                _lastColorPickerViewModel = vm;
                vm.OnColorPickerOpened();
            }
        }
    }

    private void OnColorPickerLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is ColorPicker colorPicker)
        {
            var vm = colorPicker.DataContext as PropertyItemViewModel;
            if (vm == null)
                return;

            // Only close if this was our tracked picker
            if (vm == _lastColorPickerViewModel)
            {
                vm.OnColorPickerClosed();
                _lastColorPickerViewModel = null;
            }
        }
    }
}

