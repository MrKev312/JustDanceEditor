using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal static class TimelinePackageSaver
{
    public static void Save(
        TimelineEditorViewModel timeline,
        IReadOnlyDictionary<(string id, bool isFullBody), MoveDefinitionViewModel> moveDefinitions,
        string baseTitle)
    {
        foreach (KeyValuePair<(string id, bool isFullBody), MoveDefinitionViewModel> kv in moveDefinitions)
        {
            string id = kv.Key.id;
            bool isFull = kv.Key.isFullBody;
            MoveDefinitionViewModel def = kv.Value;

            CoachMoveDefinition coachDef = new()
            {
                Color = ClipViewModel.ColorToRgbHex(def.Color),
                Duration = (int)def.DefaultDuration,
                MoveType = isFull ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking
            };

            if (isFull)
                timeline.Package.FullBodyCoachMoves[id] = coachDef;
            else
                timeline.Package.HandCoachMoves[id] = coachDef;
        }

        foreach (TrackViewModel track in timeline.Tracks)
        {
            switch (track.TrackType)
            {
                case TrackType.Lyrics:
                    timeline.Package.Lyrics.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is KaraokeClip kc)
                            timeline.Package.Lyrics.Clips.Add(kc);
                    break;
                case TrackType.Pictogram:
                    timeline.Package.Pictograms.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is PictogramClip pc)
                            timeline.Package.Pictograms.Clips.Add(pc);
                    break;
                case TrackType.HideHud:
                    timeline.Package.HideUserInterface.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is HideUserInterfaceClip hic)
                            timeline.Package.HideUserInterface.Clips.Add(hic);
                    break;
                case TrackType.GoldEffect:
                    timeline.Package.GoldEffects.Clips.Clear();
                    foreach (ClipViewModel c in track.Clips)
                        if (c.RawClip is GoldEffectClip gc)
                            timeline.Package.GoldEffects.Clips.Add(gc);
                    break;
                case TrackType.CoachHand:
                    SyncCoachTimeline(track, timeline.Package.CoachTimelines, "Coach ");
                    break;
                case TrackType.CoachFullBody:
                    SyncCoachTimeline(track, timeline.Package.FullBodyCoachTimelines, "FullBody Coach ");
                    break;
            }
        }

        timeline.Package.Metadata.LyricsColor = ClipViewModel.ColorToRgbaHex(timeline.LyricsDefinitionColor);
        IntermediatePackageSerializer.WriteToFolder(timeline.Package, timeline.RootPath);

        timeline.UndoService.MarkSaved();
        timeline.Title = baseTitle;
    }

    private static void SyncCoachTimeline(TrackViewModel track, IReadOnlyList<MoveTimeline> timelines, string titlePrefix)
    {
        string idStr = track.Title.StartsWith(titlePrefix) ? track.Title[titlePrefix.Length..] : string.Empty;
        if (!int.TryParse(idStr, out int coachId))
            return;

        MoveTimeline? moveTimeline = timelines.FirstOrDefault(t => t.CoachId == coachId);
        if (moveTimeline == null)
            return;

        moveTimeline.Clips.Clear();
        foreach (ClipViewModel c in track.Clips)
        {
            if (c.RawClip is MoveClip mc)
                moveTimeline.Clips.Add(mc);
        }
    }
}