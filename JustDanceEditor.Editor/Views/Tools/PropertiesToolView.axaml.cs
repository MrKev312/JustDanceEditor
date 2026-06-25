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
                TopLevel? topLevel = TopLevel.GetTopLevel(this);
                _ = topLevel?.FocusManager?.Focus(this, NavigationMethod.Unspecified, KeyModifiers.None);
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
                    Focus();
                }

                e.Handled = true;
            }
        }
    }

    private void OnColorPickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Note: This event fires for ANY property change on the ColorPicker, including Color binding changes
        // We should NOT use this to track open/close - only LostFocus and GotFocus should do that
        // If we need to track live color changes, add a new event handler for GotFocus instead
    }

    private void OnColorPickerGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Open the color picker once when it gets focus
        if (sender is ColorPicker colorPicker)
        {
            if (colorPicker.DataContext is not PropertyItemViewModel vm)
                return;

            // Only open if this is a new color picker
            if (vm != _lastColorPickerViewModel)
            {
                // Close the previous color picker if there was one
                _lastColorPickerViewModel?.OnColorPickerClosed();

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
            if (colorPicker.DataContext is not PropertyItemViewModel vm)
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