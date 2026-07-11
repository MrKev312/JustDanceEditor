using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal static class TimelineTrackBuilder
{
    public static Color Build(
        TimelineEditorViewModel timeline,
        Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> moveDefinitions)
    {
        Color lyricsColor = Colors.Yellow;
        if (!string.IsNullOrEmpty(timeline.Package.Metadata.LyricsColor))
            lyricsColor = ClipViewModel.ParseRgbaHex(timeline.Package.Metadata.LyricsColor);

        Color lyricsDefinitionColor = new(255, lyricsColor.R, lyricsColor.G, lyricsColor.B);

        foreach (KeyValuePair<string, CoachMoveDefinition> kv in timeline.Package.HandCoachMoves)
            moveDefinitions[(kv.Key, false)] = CreateMoveDefinition(kv.Key, kv.Value, isFullBody: false);

        foreach (KeyValuePair<string, CoachMoveDefinition> kv in timeline.Package.FullBodyCoachMoves)
            moveDefinitions[(kv.Key, true)] = CreateMoveDefinition(kv.Key, kv.Value, isFullBody: true);

        AddTrack(timeline, "Video", 40, Colors.IndianRed, TrackType.Video, []);
        AddTrack(timeline, "Hide HUD", 30, Colors.MediumPurple, TrackType.HideHud, timeline.Package.HideUserInterface.Clips.Cast<TimelineClipBase>());
        AddTrack(timeline, "Lyrics", 40, Colors.Goldenrod, TrackType.Lyrics, timeline.Package.Lyrics.Clips.Cast<TimelineClipBase>());
        AddTrack(timeline, "Pictograms", 60, Colors.CornflowerBlue, TrackType.Pictogram, timeline.Package.Pictograms.Clips.Cast<TimelineClipBase>());

        foreach (MoveTimeline coachTimeline in timeline.Package.CoachTimelines)
            AddTrack(timeline, $"Coach {coachTimeline.CoachId}", 40, Colors.MediumPurple, TrackType.CoachHand, coachTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: false);

        foreach (MoveTimeline fullBodyTimeline in timeline.Package.FullBodyCoachTimelines)
            AddTrack(timeline, $"FullBody Coach {fullBodyTimeline.CoachId}", 60, Colors.SeaGreen, TrackType.CoachFullBody, fullBodyTimeline.Clips.Cast<TimelineClipBase>(), isFullBody: true);

        AddTrack(timeline, "Gold Effects", 30, Colors.OrangeRed, TrackType.GoldEffect, timeline.Package.GoldEffects.Clips.Cast<TimelineClipBase>());

        return lyricsDefinitionColor;
    }

    private static MoveDefinitionViewModel CreateMoveDefinition(string id, CoachMoveDefinition definition, bool isFullBody)
        => new()
        {
            Id = id,
            IsFullBody = isFullBody,
            DefaultDuration = definition.Duration <= 0 ? 24.0 : definition.Duration,
            Color = ClipViewModel.ParseRgbaHex(definition.Color)
        };

    private static void AddTrack(
        TimelineEditorViewModel timeline,
        string title,
        double height,
        Color color,
        TrackType trackType,
        IEnumerable<TimelineClipBase> clips,
        bool isFullBody = false)
    {
        TrackViewModel track = new() { Title = title, Height = height, TrackColor = color, TrackType = trackType };
        foreach (TimelineClipBase clip in clips)
        {
            switch (clip)
            {
                case HideUserInterfaceClip hic:
                    track.Clips.Add(new HideUserInterfaceClipViewModel(hic, timeline.RootPath, timeline));
                    break;
                case KaraokeClip kc:
                    track.Clips.Add(new KaraokeClipViewModel(kc, timeline.RootPath, timeline));
                    break;
                case PictogramClip pc:
                    track.Clips.Add(new PictogramClipViewModel(pc, timeline.RootPath, timeline));
                    break;
                case MoveClip mc:
                    MoveDefinitionViewModel defVm = timeline.GetOrRegisterMove(mc.MoveId, isFullBody);
                    track.Clips.Add(new MoveClipViewModel(mc, timeline.RootPath, timeline, isFullBody, (int)defVm.DefaultDuration));
                    break;
                case GoldEffectClip gc:
                    track.Clips.Add(new GoldEffectClipViewModel(gc, timeline.RootPath, timeline));
                    break;
                default:
                    track.Clips.Add(new GoldEffectClipViewModel(new GoldEffectClip(), timeline.RootPath, timeline));
                    break;
            }
        }

        timeline.Tracks.Add(track);
    }
}
