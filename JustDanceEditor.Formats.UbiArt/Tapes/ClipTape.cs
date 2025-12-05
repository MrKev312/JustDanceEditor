using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

namespace JustDanceEditor.Formats.UbiArt.Tapes;

public sealed record ClipTape
{
    public Clip[] Clips { get; set; } = [];
}