using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Model;

public sealed record ClipTape
{
    public Clip[] Clips { get; set; } = [];
}