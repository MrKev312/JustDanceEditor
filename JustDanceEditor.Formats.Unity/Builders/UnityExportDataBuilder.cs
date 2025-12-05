using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Models;

namespace JustDanceEditor.Formats.Unity.Builders;

public static class UnityExportDataBuilder
{
    public static UnityExportData Create(IntermediateSongPackage package)
    {
        IntermediateMetadata metadata = package.Metadata;
        metadata.Validate();

        UnityExportMetadata exportMetadata = new()
        {
            SongId = metadata.SongId,
            Artist = metadata.Artist ?? string.Empty,
            CoachCount = metadata.CoachCount,
            CoachNamesLocIds = metadata.CoachNames?.ToArray() ?? [],
            Credits = metadata.Credits ?? string.Empty,
            DanceVersionLocId = 0,
            Difficulty = metadata.Difficulty,
            // Todo: DoubleScoringType: do we have gestures?
            HasSongTitleInCover = metadata.HasSongTitleInCover,
            LyricsColor = string.IsNullOrWhiteSpace(metadata.LyricsColor) ? "#FFFFFFFF" : metadata.LyricsColor!,
            MapLength = metadata.MapLengthSeconds,
            MapName = metadata.MapName,
            OriginalJdVersion = metadata.OriginalJdVersion,
            ParentMapName = metadata.ParentMapName,
            SweatDifficulty = metadata.SweatDifficulty,
            TagIds = [],
            Tags = metadata.Tags?.ToArray() ?? [],
            Title = metadata.Title ?? string.Empty,
        };

        string name = string.IsNullOrWhiteSpace(metadata.MapName) 
            ? metadata.Title ?? string.Empty 
            : metadata.MapName;
        
        if (string.IsNullOrWhiteSpace(name))
            name = "Song";

        return new UnityExportData(
            name,
            exportMetadata,
            package.TimelineStructure ?? new(),
            BuildOrderedClips(package.Lyrics?.Clips),
            BuildOrderedClips(package.Pictograms?.Entries),
            BuildMotionClips(package),
            BuildOrderedClips(package.GoldEffects?.Clips),
            BuildOrderedClips(package.HideUserInterface?.Clips));
    }

    private static IReadOnlyList<T> BuildOrderedClips<T>(List<T>? clips) where T : TimelineClipBase
    {
        return clips?.OrderBy(c => c.StartTime).ToList() ?? (IReadOnlyList<T>)[];
    }

    private static IReadOnlyList<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> BuildMotionClips(IntermediateSongPackage package)
    {
        if (package.CoachTimelines == null && package.FullBodyCoachTimelines == null)
            return [];

        List<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> clips = [];

        foreach ((CoachTimelineDocument timeline, CoachMoveType moveType) in EnumerateCoachTimelines(package).OrderBy(entry => entry.Timeline.CoachId))
        {
            foreach (CoachTimelineClip clip in timeline.Clips.OrderBy(c => c.StartTime))
            {
                CoachMoveDefinition? definition = FindMoveDefinition(package, clip.MoveId, moveType);
                
                if (definition == null)
                    Logging.Logger.Log($"Move definition not found for move ID '{clip.MoveId}' (Coach ID: {timeline.CoachId}). Using default duration.", Logging.LogLevel.Warning);

                int duration = definition?.Duration ?? 48;
                int moveTypeValue = moveType == CoachMoveType.FullBodyTracking ? 1 : 0;

                clips.Add((clip, timeline.CoachId, timeline.TrackId, moveTypeValue, duration));
            }
        }

        return clips;
    }

    private static IEnumerable<(CoachTimelineDocument Timeline, CoachMoveType MoveType)> EnumerateCoachTimelines(IntermediateSongPackage package)
    {
        if (package.CoachTimelines != null)
        {
            foreach (CoachTimelineDocument timeline in package.CoachTimelines)
                yield return (timeline, CoachMoveType.HandTracking);
        }

        if (package.FullBodyCoachTimelines != null)
        {
            foreach (CoachTimelineDocument timeline in package.FullBodyCoachTimelines)
                yield return (timeline, CoachMoveType.FullBodyTracking);
        }
    }

    private static CoachMoveDefinition? FindMoveDefinition(IntermediateSongPackage package, string moveId, CoachMoveType preferredType)
    {
        if (string.IsNullOrWhiteSpace(moveId))
            return null;

        CoachMoveDefinition? definition = TryLookupMoveDefinition(package, moveId, preferredType);
        if (definition != null)
            return definition;

        CoachMoveType alternate = preferredType == CoachMoveType.FullBodyTracking 
            ? CoachMoveType.HandTracking 
            : CoachMoveType.FullBodyTracking;
        
        return TryLookupMoveDefinition(package, moveId, alternate);
    }

    private static CoachMoveDefinition? TryLookupMoveDefinition(IntermediateSongPackage package, string moveId, CoachMoveType moveType)
    {
        Dictionary<string, CoachMoveDefinition>? catalog = moveType == CoachMoveType.FullBodyTracking
            ? package.FullBodyCoachMoves
            : package.HandCoachMoves;

        return catalog?.GetValueOrDefault(moveId);
    }
}
