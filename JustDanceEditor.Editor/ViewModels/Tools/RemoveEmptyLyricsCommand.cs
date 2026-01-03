using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Remove Empty Lyrics", "Timeline")]
public class RemoveEmptyLyricsCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext)
    {
        var timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return false;

        return timeline.Tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().Any(k => string.IsNullOrWhiteSpace(k.Lyrics));
    }

    public void Run(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        // Collect removal info across all tracks
        List<(TrackViewModel Track, KaraokeClipViewModel Clip, int Index, KaraokeClipViewModel? PrevClip, bool PrevWasEnd)> removals = [];

        foreach (TrackViewModel track in timeline.Tracks)
        {
            // Build list of karaoke clips with indices
            List<(KaraokeClipViewModel Clip, int Index)> karaokeWithIndex = track.Clips
                .Select((c, idx) => (Clip: c, Index: idx))
                .Where(t => t.Clip is KaraokeClipViewModel)
                .Select(t => (Clip: (KaraokeClipViewModel)t.Clip, Index: t.Index))
                .ToList();

            // Find empty ones
            List<(KaraokeClipViewModel Clip, int Index)> emptyOnes = karaokeWithIndex.Where(k => string.IsNullOrWhiteSpace(k.Clip.Lyrics)).ToList();
            if (emptyOnes.Count == 0)
                continue;

            // Process in descending index order so removals don't shift earlier indices
            foreach (var e in emptyOnes.OrderByDescending(x => x.Index))
            {
                int idx = e.Index;
                KaraokeClipViewModel clip = e.Clip;

                // Find previous karaoke clip before this index
                KaraokeClipViewModel? prev = track.Clips.Take(idx).LastOrDefault(c => c is KaraokeClipViewModel) as KaraokeClipViewModel;
                bool prevWasEnd = prev?.IsEndOfLine ?? false;

                // Record removal info
                removals.Add((track, clip, idx, prev, prevWasEnd));

                // Remove it
                track.Clips.Remove(clip);

                // If it was end of line, set previous to end of line
                if (clip.IsEndOfLine && prev != null)
                {
                    prev.IsEndOfLine = true;
                }
            }
        }

        if (removals.Count == 0)
            return;

        // Prepare undo/redo actions
        timeline.UndoService.Record(
            undo: () =>
            {
                // Re-insert removed clips at their original indices and restore prev end flags
                foreach (var info in removals.OrderBy(r => r.Index))
                {
                    var track = info.Track;
                    int idx = Math.Min(info.Index, track.Clips.Count);
                    if (!track.Clips.Contains(info.Clip))
                        track.Clips.Insert(idx, info.Clip);

                    if (info.PrevClip != null)
                        info.PrevClip.IsEndOfLine = info.PrevWasEnd;
                }
            },
            redo: () =>
            {
                // Remove the clips again and set prev end-of-line where appropriate
                foreach (var info in removals.OrderByDescending(r => r.Index))
                {
                    var track = info.Track;
                    if (track.Clips.Contains(info.Clip))
                        track.Clips.Remove(info.Clip);

                    if (info.Clip.IsEndOfLine && info.PrevClip != null)
                        info.PrevClip.IsEndOfLine = true;
                }
            }
        );
    }
}
