using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineSelectionController(TimelineEditorViewModel timeline)
{
    public IReadOnlyList<PictogramClipViewModel> SelectAllPictogramInstances(string pictogramId)
    {
        pictogramId = pictogramId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pictogramId))
            return [];

        List<PictogramClipViewModel> matches = [.. timeline.Tracks
            .SelectMany(t => t.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(c => string.Equals(c.PictogramId, pictogramId, StringComparison.OrdinalIgnoreCase))];

        ApplySelection(matches);
        return matches;
    }

    public IReadOnlyList<MoveClipViewModel> SelectAllMoveInstances(string moveId)
    {
        moveId = moveId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moveId))
            return [];

        List<MoveClipViewModel> matches = [.. timeline.Tracks
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => string.Equals(c.MoveId, moveId, StringComparison.OrdinalIgnoreCase))];

        ApplySelection(matches);
        return matches;
    }

    private void ApplySelection<TClip>(IReadOnlyCollection<TClip> selectedClips)
        where TClip : ClipViewModel
    {
        HashSet<TClip> selected = [.. selectedClips];
        foreach (ClipViewModel clip in timeline.Tracks.SelectMany(t => t.Clips))
            clip.IsSelected = clip is TClip typedClip && selected.Contains(typedClip);
    }
}