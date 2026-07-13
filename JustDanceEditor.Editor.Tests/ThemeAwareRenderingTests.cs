using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

using JustDanceEditor.Editor.Converters;
using JustDanceEditor.Editor.Views.Timeline;
using JustDanceEditor.Editor.Views.Tools;

using KevInc.Avalonia;
using KevInc.Avalonia.Timeline;

namespace JustDanceEditor.Editor.Tests;

public sealed class ThemeAwareRenderingTests
{
    [AvaloniaFact]
    public void TimelinePaletteUpdatesWhileGraphSeriesColorsStayConsistent()
    {
        TestGraphControl graph = new();
        TimeRulerControl ruler = new();
        Window window = new()
        {
            Content = new StackPanel { Children = { graph, ruler } },
            RequestedThemeVariant = ThemeVariant.Light
        };

        window.Show();

        Assert.Equal(Colors.Black, TimelineResources.LabelBrush.Color);
        Assert.Equal(Color.Parse("#526579"), TimelineResources.WaveformFill.Color);
        Assert.Equal(Color.FromRgb(88, 204, 255), graph.SeriesColor(0));
        Assert.Equal(125, graph.SeriesColor(0, 125).A);

        window.RequestedThemeVariant = ThemeVariant.Dark;

        Assert.Equal(Colors.LightGray, TimelineResources.LabelBrush.Color);
        Assert.Equal(Colors.LightGray, TimelineResources.WaveformFill.Color);
        Assert.Equal(Color.FromRgb(88, 204, 255), graph.SeriesColor(0));
        Assert.Equal(125, graph.SeriesColor(0, 125).A);

        window.Close();
    }

    [Fact]
    public void ZoomedOutBeatLabels_StayOnOneRegularGlobalGrid()
    {
        int interval = TimeRulerControl.GetLabelBeatInterval(3);

        Assert.Equal(16, interval);
        Assert.True(TimeRulerControl.ShouldDrawBeatLabel(0, interval));
        Assert.True(TimeRulerControl.ShouldDrawBeatLabel(16, interval));
        Assert.True(TimeRulerControl.ShouldDrawBeatLabel(32, interval));
        Assert.False(TimeRulerControl.ShouldDrawBeatLabel(20, interval));
        Assert.False(TimeRulerControl.ShouldDrawBeatLabel(36, interval));
    }

    [Fact]
    public void ActiveStepIndicator_UsesLivePlatformAccentBrush()
    {
        BoolToStepBrushConverter converter = new();

        object brush = converter.Convert(true, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Same(PlatformTheme.SystemAccentBrush, brush);
    }

    private sealed class TestGraphControl : ThemeAwareGraphControl
    {
        public Color SeriesColor(int index) => GetSeriesColor(index, byte.MaxValue);
        public Color SeriesColor(int index, byte alpha) => GetSeriesColor(index, alpha);
    }
}