using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineClipboardController(TimelineEditorViewModel timeline)
{
    private readonly List<ClipClipboardEntry> _clipClipboard = [];

    public bool CanPasteCopiedClips => _clipClipboard.Count > 0;

    public void DeleteSelectedClips()
    {
        List<(TrackViewModel Track, ClipViewModel Clip)> toDelete = [];
        foreach (TrackViewModel track in timeline.Tracks)
        {
            foreach (ClipViewModel clip in track.Clips)
            {
                if (clip.IsSelected)
                    toDelete.Add((track, clip));
            }
        }

        if (toDelete.Count == 0)
            return;

        timeline.PushUndo(
            undo: () =>
            {
                foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
                {
                    if (!Track.Clips.Contains(Clip))
                        Track.Clips.Add(Clip);
                }
            },
            redo: () =>
            {
                foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
                    Track.Clips.Remove(Clip);
            });

        foreach ((TrackViewModel Track, ClipViewModel Clip) in toDelete)
            Track.Clips.Remove(Clip);
    }

    public void CopySelectedClips()
    {
        List<(int TrackIndex, ClipViewModel Clip)> selected = GetSelectedClipEntries();
        if (selected.Count == 0)
            return;

        double earliestStartBeat = selected.Min(s => s.Clip.StartBeat);
        List<ClipClipboardEntry> clipboard = [];

        foreach ((int trackIndex, ClipViewModel clip) in selected)
        {
            ClipClipboardPayload? payload = CreateClipboardPayload(clip);
            if (payload == null)
                continue;

            TrackViewModel sourceTrack = timeline.Tracks[trackIndex];
            clipboard.Add(new ClipClipboardEntry(
                trackIndex,
                sourceTrack.TrackType,
                sourceTrack.Title,
                clip.StartBeat - earliestStartBeat,
                payload));
        }

        if (clipboard.Count == 0)
            return;

        _clipClipboard.Clear();
        _clipClipboard.AddRange(clipboard);
    }

    public void PasteCopiedClips()
    {
        if (_clipClipboard.Count == 0)
            return;

        List<ClipViewModel> previousSelection = [.. timeline.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected)];
        double pasteAnchor = double.IsFinite(timeline.CurrentBeat)
            ? timeline.CurrentBeat
            : timeline.TimelineStructure.StartBeat;

        List<(TrackViewModel Track, ClipViewModel Clip)> inserted = [];
        foreach (ClipClipboardEntry entry in _clipClipboard)
        {
            TrackViewModel? targetTrack = ResolvePasteTrack(entry);
            if (targetTrack == null)
                continue;

            ClipViewModel created = entry.Payload.CreateViewModel(this, timeline, pasteAnchor + entry.RelativeStartBeat);
            created.IsSelected = false;

            targetTrack.Clips.Add(created);
            inserted.Add((targetTrack, created));
        }

        if (inserted.Count == 0)
            return;

        List<ClipViewModel> insertedClips = [.. inserted.Select(i => i.Clip)];

        timeline.PushUndo(
            undo: () =>
            {
                foreach ((TrackViewModel track, ClipViewModel clip) in inserted)
                    track.Clips.Remove(clip);

                SetSelectedClips(previousSelection);
            },
            redo: () =>
            {
                foreach ((TrackViewModel track, ClipViewModel clip) in inserted)
                {
                    if (!track.Clips.Contains(clip))
                        track.Clips.Add(clip);
                }

                SetSelectedClips(insertedClips);
            });

        SetSelectedClips(insertedClips);
    }

    public void SelectAndCenterClip(ClipViewModel clip)
    {
        SetSelectedClips([clip]);
        timeline.RequestedCenterBeat = clip.StartBeat + (clip.DurationBeats / 2.0);
        timeline.CenterBeatRequestVersion++;
    }

    private List<(int TrackIndex, ClipViewModel Clip)> GetSelectedClipEntries()
    {
        List<(int TrackIndex, ClipViewModel Clip)> selected = [];
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            foreach (ClipViewModel clip in timeline.Tracks[i].Clips.Where(c => c.IsSelected))
                selected.Add((i, clip));
        }

        selected.Sort(static (a, b) =>
        {
            int trackCompare = a.TrackIndex.CompareTo(b.TrackIndex);
            return trackCompare != 0 ? trackCompare : a.Clip.StartBeat.CompareTo(b.Clip.StartBeat);
        });

        return selected;
    }

    private static KaraokeTolerance? CloneTolerance(KaraokeTolerance? tolerance)
    {
        if (tolerance == null)
            return null;

        return new KaraokeTolerance
        {
            StartTimeTolerance = tolerance.StartTimeTolerance,
            EndTimeTolerance = tolerance.EndTimeTolerance,
            SemitoneTolerance = tolerance.SemitoneTolerance
        };
    }

    private static ClipClipboardPayload? CreateClipboardPayload(ClipViewModel clip)
    {
        return clip switch
        {
            PictogramClipViewModel pictogram => new PictogramClipboardPayload(
                pictogram.PictogramId,
                ((PictogramClip)pictogram.RawClip).Duration,
                ((PictogramClip)pictogram.RawClip).CoachCount),
            KaraokeClipViewModel karaoke => new KaraokeClipboardPayload(
                karaoke.Lyrics,
                ((KaraokeClip)karaoke.RawClip).Duration,
                ((KaraokeClip)karaoke.RawClip).Pitch,
                karaoke.IsEndOfLine,
                ((KaraokeClip)karaoke.RawClip).ContentType,
                CloneTolerance(((KaraokeClip)karaoke.RawClip).Tolerances)),
            HideUserInterfaceClipViewModel hideHud => new HideHudClipboardPayload(
                ((HideUserInterfaceClip)hideHud.RawClip).Duration,
                ((HideUserInterfaceClip)hideHud.RawClip).IsActive),
            GoldEffectClipViewModel gold => new GoldEffectClipboardPayload(
                ((GoldEffectClip)gold.RawClip).TrackId,
                ((GoldEffectClip)gold.RawClip).Duration,
                ((GoldEffectClip)gold.RawClip).IsActive,
                ((GoldEffectClip)gold.RawClip).EffectType),
            MoveClipViewModel move => new MoveClipboardPayload(move.MoveId, move.IsGoldMove, move.IsFullBody),
            _ => null
        };
    }

    private TrackViewModel? ResolvePasteTrack(ClipClipboardEntry entry)
    {
        if (entry.TrackIndex >= 0 && entry.TrackIndex < timeline.Tracks.Count)
        {
            TrackViewModel sameIndexTrack = timeline.Tracks[entry.TrackIndex];
            if (sameIndexTrack.TrackType == entry.TrackType)
                return sameIndexTrack;
        }

        return timeline.Tracks.FirstOrDefault(t => t.TrackType == entry.TrackType && t.Title == entry.TrackTitle)
            ?? timeline.Tracks.FirstOrDefault(t => t.TrackType == entry.TrackType);
    }

    private void SetSelectedClips(IEnumerable<ClipViewModel> selected)
    {
        HashSet<ClipViewModel> selectedSet = [.. selected];
        foreach (ClipViewModel clip in timeline.Tracks.SelectMany(t => t.Clips))
            clip.IsSelected = selectedSet.Contains(clip);

        if (timeline.Services.TimelineContext != null)
        {
            timeline.Services.TimelineContext.SelectedObjects =
                [.. timeline.Tracks.SelectMany(t => t.Clips).Where(c => c.IsSelected).Cast<object>()];
        }
    }

    private ClipViewModel ClampAndCreate(ClipViewModel clip, double desiredStartBeat)
    {
        double minBeat = timeline.TimelineStructure.StartBeat;
        double maxStartBeat = timeline.TimelineStructure.EndBeat - clip.DurationBeats;
        if (maxStartBeat < minBeat)
            maxStartBeat = minBeat;

        double clampedStartBeat = Math.Max(minBeat, Math.Min(desiredStartBeat, maxStartBeat));
        clip.StartBeat = clampedStartBeat;
        return clip;
    }

    private sealed record ClipClipboardEntry(int TrackIndex, TrackType TrackType, string TrackTitle, double RelativeStartBeat, ClipClipboardPayload Payload);

    private abstract record ClipClipboardPayload
    {
        public abstract ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat);
    }

    private sealed record PictogramClipboardPayload(string PictogramId, int DurationFrames, int CoachCount) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat)
        {
            PictogramClip raw = new()
            {
                PictogramId = PictogramId,
                Duration = DurationFrames,
                CoachCount = CoachCount,
                StartTime = 0
            };

            return clipboard.ClampAndCreate(new PictogramClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record KaraokeClipboardPayload(string Lyrics, int DurationFrames, float Pitch, bool IsEndOfLine, int ContentType, KaraokeTolerance? Tolerance) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat)
        {
            KaraokeClip raw = new()
            {
                Lyrics = Lyrics,
                Duration = DurationFrames,
                Pitch = Pitch,
                IsEndOfLine = IsEndOfLine,
                ContentType = ContentType,
                Tolerances = CloneTolerance(Tolerance),
                StartTime = 0
            };

            return clipboard.ClampAndCreate(new KaraokeClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record HideHudClipboardPayload(int DurationFrames, bool IsActive) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat)
        {
            HideUserInterfaceClip raw = new()
            {
                Duration = DurationFrames,
                IsActive = IsActive,
                StartTime = 0
            };

            return clipboard.ClampAndCreate(new HideUserInterfaceClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record GoldEffectClipboardPayload(long TrackId, int DurationFrames, bool IsActive, int EffectType) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat)
        {
            GoldEffectClip raw = new()
            {
                TrackId = TrackId,
                Duration = DurationFrames,
                IsActive = IsActive,
                EffectType = EffectType,
                StartTime = 0
            };

            return clipboard.ClampAndCreate(new GoldEffectClipViewModel(raw, timeline.RootPath, timeline), startBeat);
        }
    }

    private sealed record MoveClipboardPayload(string MoveId, bool IsGoldMove, bool IsFullBody) : ClipClipboardPayload
    {
        public override ClipViewModel CreateViewModel(TimelineClipboardController clipboard, TimelineEditorViewModel timeline, double startBeat)
        {
            MoveClip raw = new()
            {
                MoveId = MoveId,
                IsGoldMove = IsGoldMove,
                StartTime = 0
            };

            MoveClipViewModel vm = new(raw, timeline.RootPath, timeline, IsFullBody);
            return clipboard.ClampAndCreate(vm, startBeat);
        }
    }
}
