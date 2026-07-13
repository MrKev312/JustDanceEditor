using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public sealed class InspectablePropertyRegistry
{
    private readonly IReadOnlyList<IInspectablePropertyDescriptor> _descriptors;

    public InspectablePropertyRegistry()
    {
        _descriptors =
        [
            new InspectablePropertyDescriptor<ClipViewModel, double>(
                nameof(ClipViewModel.StartBeat), "Start Beat", "Timing",
                static clip => clip.StartBeat,
                static (clip, value) => clip.StartBeat = value),
            new InspectablePropertyDescriptor<ClipViewModel, double>(
                nameof(ClipViewModel.DurationBeats), "Duration", "Timing",
                static clip => clip.DurationBeats,
                static (clip, value) => clip.DurationBeats = value),
            new InspectablePropertyDescriptor<MoveClipViewModel, string>(
                nameof(MoveClipViewModel.MoveId), "Move Id", "Move",
                static clip => clip.MoveId,
                static (clip, value) => clip.MoveId = value,
                options: static (clip, timeline) => clip.GetAvailableMoveIds(timeline),
                isEditable: false),
            new InspectablePropertyDescriptor<MoveClipViewModel, bool>(
                nameof(MoveClipViewModel.IsGoldMove), "Gold Move", "Move",
                static clip => clip.IsGoldMove,
                static (clip, value) => clip.IsGoldMove = value),
            new MappedInspectablePropertyDescriptor<MoveClipViewModel, MoveDefinitionViewModel, Color>(
                nameof(MoveClipViewModel.BackgroundColor),
                nameof(MoveDefinitionViewModel.Color),
                "Color", "Appearance",
                static (clip, _) => clip.Definition,
                static definition => definition.Color,
                static (definition, value) => definition.Color = value),
            new InspectablePropertyDescriptor<KaraokeClipViewModel, string>(
                nameof(KaraokeClipViewModel.Lyrics), "Lyrics", "Karaoke",
                static clip => clip.Lyrics,
                static (clip, value) => clip.Lyrics = value),
            new InspectablePropertyDescriptor<KaraokeClipViewModel, bool>(
                nameof(KaraokeClipViewModel.IsEndOfLine), "End of Line", "Karaoke",
                static clip => clip.IsEndOfLine,
                static (clip, value) => clip.IsEndOfLine = value),
            new MappedInspectablePropertyDescriptor<KaraokeClipViewModel, TimelineEditorViewModel, Color>(
                nameof(KaraokeClipViewModel.BackgroundColor),
                nameof(TimelineEditorViewModel.LyricsDefinitionColor),
                "Color", "Appearance",
                static (_, timeline) => timeline,
                static timeline => timeline.LyricsDefinitionColor,
                static (timeline, value) => timeline.LyricsDefinitionColor = value,
                PropertyColorEncoding.Rgba),
            new InspectablePropertyDescriptor<PictogramClipViewModel, string>(
                nameof(PictogramClipViewModel.PictogramId), "Pictogram Id", "Pictogram",
                static clip => clip.PictogramId,
                static (clip, value) => clip.PictogramId = value,
                options: static (clip, timeline) => clip.GetAvailablePictograms(timeline))
        ];
    }

    public IReadOnlyList<ResolvedInspectableProperty> Resolve(
        IReadOnlyList<object> selection,
        TimelineEditorViewModel timeline)
        => [.. _descriptors
            .Select(descriptor => descriptor.TryResolve(selection, timeline))
            .OfType<ResolvedInspectableProperty>()];
}