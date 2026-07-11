using Avalonia.Media;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineLyricsController(TimelineEditorViewModel timeline)
{
    public Color DefinitionColor { get; private set; } = Colors.Yellow;

    public void SetDefinitionColor(Color color, bool updateMetadata)
    {
        Color normalized = NormalizeOpaque(color);
        if (!Equals(DefinitionColor, normalized))
        {
            DefinitionColor = normalized;
            if (updateMetadata)
                timeline.Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(DefinitionColor);

            timeline.NotifyLyricsColorChanged();
            return;
        }

        if (updateMetadata)
            timeline.Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(DefinitionColor);
    }

    public void UpdateLyricsColor(string rgbaHexColor)
    {
        SetDefinitionColor(ClipViewModel.ParseRgbaHex(rgbaHexColor), updateMetadata: true);
    }

    private static Color NormalizeOpaque(Color color) => new(255, color.R, color.G, color.B);
}