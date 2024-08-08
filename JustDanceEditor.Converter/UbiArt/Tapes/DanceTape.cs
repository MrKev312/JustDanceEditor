using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

namespace JustDanceEditor.Converter.UbiArt.Tapes;

public class ClipTape
{
    public string __class { get; set; } = "";
    public IClip[] Clips { get; set; } = [];
    public int TapeClock { get; set; }
    public int TapeBarCount { get; set; }
    public int FreeResourcesAfterPlay { get; set; }
    public string MapName { get; set; } = "";
    public string SoundwichEvent { get; set; } = "";
}