using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class ScoringAdjustmentToolView : UserControl
{
    public ScoringAdjustmentToolView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateResponsiveLayout();
    }

    private void OnParameterControlGotFocus(object? sender, RoutedEventArgs e)
    {
        SelectParameterForControl(sender);
    }

    private void OnParameterControlPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectParameterForControl(sender);
    }

    private void SelectParameterForControl(object? sender)
    {
        if (sender is not Control control || DataContext is not ScoringAdjustmentToolViewModel viewModel)
            return;

        string? name = control.Name;
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.StartsWith(nameof(ScoringAdjustmentParameter.LowThreshold), StringComparison.Ordinal))
            viewModel.SelectParameter(nameof(ScoringAdjustmentParameter.LowThreshold));
        else if (name.StartsWith(nameof(ScoringAdjustmentParameter.HighThreshold), StringComparison.Ordinal))
            viewModel.SelectParameter(nameof(ScoringAdjustmentParameter.HighThreshold));
        else if (name.StartsWith(nameof(ScoringAdjustmentParameter.AutoCorrelationThreshold), StringComparison.Ordinal))
            viewModel.SelectParameter(nameof(ScoringAdjustmentParameter.AutoCorrelationThreshold));
        else if (name.StartsWith(nameof(ScoringAdjustmentParameter.DirectionImpactFactor), StringComparison.Ordinal))
            viewModel.SelectParameter(nameof(ScoringAdjustmentParameter.DirectionImpactFactor));
    }

    private void UpdateResponsiveLayout()
    {
        if (DataContext is ScoringAdjustmentToolViewModel viewModel)
            viewModel.IsWideLayout = Bounds.Width >= 880;
    }
}