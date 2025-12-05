using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using IntermediateKaraokeClip = JustDanceEditor.Formats.JDI.Timelines.KaraokeClip;
using UbiArtKaraokeClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.KaraokeClip;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

internal static class IntermediatePackageBuilder
{

    public static IntermediateSongPackage FromUbiArt(ConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.SongData);

        Trackdata trackData = context.SongData.MusicTrack.COMPONENTS.FirstOrDefault()?.trackData ?? new();
        Structure structure = trackData.structure ?? new();
        TimelineMath timelineMath = new(structure);

        (
            List<CoachTimelineDocument> coachTimelines,
            List<CoachTimelineDocument> fullBodyTimelines,
            Dictionary<string, CoachMoveDefinition> handMoves,
            Dictionary<string, CoachMoveDefinition> fullBodyMoves) =
            BuildCoachTimelines(context, timelineMath);

        IntermediateSongPackage package = new()
        {
            Metadata = BuildMetadata(context, structure),
            TimelineStructure = BuildTimelineStructure(structure, timelineMath),
            Lyrics = BuildLyricsDocument(context),
            Pictograms = BuildPictogramDocument(context),
            GoldEffects = BuildGoldEffectDocument(context),
            HideUserInterface = BuildHideUserInterfaceDocument(context),
            Vibrations = BuildVibrationDocument(context),
            CoachTimelines = coachTimelines,
            FullBodyCoachTimelines = fullBodyTimelines,
            HandCoachMoves = handMoves,
            FullBodyCoachMoves = fullBodyMoves
        };

        package.Metadata.Validate();
        return package;
    }

    private static IntermediateMetadata BuildMetadata(ConversionContext context, Structure structure)
    {
        InfoComponent info = context.SongData.SongDesc.COMPONENTS.First();

        double mapLengthSeconds = CalculateMapLengthSeconds(structure);
        string lyricsColor = ConvertColor(info.DefaultColors.lyrics);

        IntermediateMetadata metadata = new()
        {
            SongID = context.Request.SongGUID,
            MapName = info.MapName,
            ParentMapName = info.MapName,
            Title = info.Title,
            Artist = info.Artist,
            Credits = info.Credits,
            LyricsColor = lyricsColor,
            MapLengthSeconds = mapLengthSeconds,
            OriginalJDVersion = context.SongData.JDVersion,
            CoachCount = info.NumCoach,
            CoachNames = null, // UbiArt does not store coach names
            Difficulty = info.Difficulty,
            SweatDifficulty = info.SweatDifficulty,
            Tags = info.Tags?.ToList() ?? [],
            Status = info.Status,
            MojoValue = info.MojoValue,
            CountInProgression = info.CountInProgression
        };

        metadata.AdditionalMetadata["platformType"] = context.FileSystem.PlatformType;
        metadata.AdditionalMetadata["videoPreviewPath"] = info.VideoPreviewPath;

        return metadata;
    }

    private static TimelineStructureDocument BuildTimelineStructure(Structure structure, TimelineMath timelineMath)
    {
        TimelineStructureDocument document = new()
        {
            TimeBaseMsPerBeat = timelineMath.EstimateMsPerBeat(),
            StartBeat = structure.startBeat,
            EndBeat = structure.endBeat,
            VideoStartOffset = structure.videoStartTime,
            PreviewEntryBeat = structure.previewEntry,
            PreviewLoopStartBeat = structure.previewLoopStart,
            PreviewLoopEndBeat = structure.previewLoopEnd,
            PrevewDuration = structure.previewDuration,
            Markers = [.. structure.markers.Select(m => (int)Math.Round(timelineMath.ToBeat(m)))]
        };

        if (structure.signatures is { Length: > 0 })
        {
            foreach (Signature signature in structure.signatures)
            {
                document.Signatures.Add(new SignatureSegment
                {
                    Beats = signature.beats,
                    Marker = signature.marker,
                    Comment = signature.comment
                });
            }
        }

        if (structure.sections is { Length: > 0 })
        {
            foreach (Section section in structure.sections)
            {
                document.Sections.Add(new SectionSegment
                {
                    StartBeat = section.marker,
                    SectionType = section.sectionType,
                    Comment = section.comment
                });
            }
        }

        document.TempoSegments.Add(new TempoSegment
        {
            StartBeat = structure.startBeat,
            BeatsPerMinute = timelineMath.EstimateBpm()
        });

        return document;
    }

    private static LyricsTimelineDocument BuildLyricsDocument(ConversionContext context)
    {
        LyricsTimelineDocument document = new();

        foreach (UbiArtKaraokeClip clip in context.SongData.Clips.OfType<UbiArtKaraokeClip>().OrderBy(c => c.StartTime))
        {
            IntermediateKaraokeClip entry = new()
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                Lyrics = clip.Lyrics,
                Pitch = clip.Pitch,
                IsEndOfLine = clip.IsEndOfLine == 1,
                ContentType = clip.ContentType
            };

            if (clip.StartTimeTolerance > 0 || clip.EndTimeTolerance > 0 || clip.SemitoneTolerance > 0)
            {
                entry.Tolerances = new KaraokeTolerance
                {
                    StartTimeTolerance = clip.StartTimeTolerance,
                    EndTimeTolerance = clip.EndTimeTolerance,
                    SemitoneTolerance = clip.SemitoneTolerance
                };
            }

            document.Clips.Add(entry);
        }

        return document;
    }

    private static PictogramTimelineDocument BuildPictogramDocument(ConversionContext context)
    {
        PictogramTimelineDocument document = new();

        foreach (PictogramClip clip in context.SongData.Clips.OfType<PictogramClip>().OrderBy(c => c.StartTime))
        {
            document.Entries.Add(new PictogramEntry
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                PictogramId = Path.GetFileNameWithoutExtension(clip.PictoPath),
                CoachCount = (int)clip.CoachCount
            });
        }

        return document;
    }

    private static GoldEffectTimelineDocument BuildGoldEffectDocument(ConversionContext context)
    {
        GoldEffectTimelineDocument document = new();

        foreach (GoldEffectClip clip in context.SongData.Clips.OfType<GoldEffectClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new GoldEffectTimelineClip
            {
                Id = clip.Id,
                TrackId = clip.TrackId,
                IsActive = clip.IsActive > 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                EffectType = clip.EffectType
            });
        }

        return document;
    }

    private static HideUserInterfaceTimelineDocument BuildHideUserInterfaceDocument(ConversionContext context)
    {
        HideUserInterfaceTimelineDocument document = new();

        foreach (HideUserInterfaceClip clip in context.SongData.Clips.OfType<HideUserInterfaceClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new HideUserInterfaceTimelineClip
            {
                Id = clip.Id,
                IsActive = clip.IsActive > 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            });
        }

        return document;
    }

    private static VibrationTimelineDocument BuildVibrationDocument(ConversionContext context)
    {
        VibrationTimelineDocument document = new();

        foreach (VibrationClip clip in context.SongData.Clips.OfType<VibrationClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new VibrationTimelineClip
            {
                Id = clip.Id,
                TrackId = clip.TrackId,
                IsActive = clip.IsActive > 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            });
        }

        return document;
    }

    private static (
        List<CoachTimelineDocument> HandTracking,
        List<CoachTimelineDocument> FullBodyTracking,
        Dictionary<string, CoachMoveDefinition> HandMoves,
        Dictionary<string, CoachMoveDefinition> FullBodyMoves) BuildCoachTimelines(ConversionContext context, TimelineMath timelineMath)
    {
        Dictionary<int, CoachTimelineDocument> handTimelines = [];
        Dictionary<int, CoachTimelineDocument> fullBodyTimelines = [];
        Dictionary<string, CoachMoveDefinition> handMoves = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CoachMoveDefinition> fullBodyMoves = new(StringComparer.OrdinalIgnoreCase);

        foreach (MotionClip clip in context.SongData.Clips.OfType<MotionClip>())
        {
            if (!clip.ClassifierPath.EndsWith(".msm", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isFullBody = clip.MoveType == 1;
            Dictionary<int, CoachTimelineDocument> target = isFullBody ? fullBodyTimelines : handTimelines;
            Dictionary<string, CoachMoveDefinition> moveCatalog = isFullBody ? fullBodyMoves : handMoves;

            if (!target.TryGetValue(clip.CoachId, out CoachTimelineDocument? timeline))
            {
                timeline = new CoachTimelineDocument { CoachId = clip.CoachId };
                target[clip.CoachId] = timeline;
            }

            string moveId = Path.GetFileNameWithoutExtension(clip.ClassifierPath).ToLowerInvariant();

            timeline.Clips.Add(new CoachTimelineClip
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                MoveId = moveId,
                IsGoldMove = clip.GoldMove == 1
            });

            AddOrUpdateMoveDefinition(
                moveCatalog,
                moveId,
                clip.Duration,
                isFullBody ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking);
        }

        return (
            handTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            fullBodyTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            handMoves,
            fullBodyMoves);
    }

    private static void AddOrUpdateMoveDefinition(
        Dictionary<string, CoachMoveDefinition> catalog,
        string moveId,
        int duration,
        CoachMoveType moveType)
    {
        if (!catalog.TryGetValue(moveId, out CoachMoveDefinition? definition))
        {
            catalog[moveId] = new CoachMoveDefinition
            {
                Duration = duration,
                MoveType = moveType
            };
            return;
        }

        if (duration > 0)
            definition.Duration = duration;

        definition.MoveType = moveType;
    }

    private static double CalculateMapLengthSeconds(Structure structure)
    {
        if (structure?.markers == null || structure.markers.Length == 0)
            return 0;

        int startIndex = Math.Clamp(Math.Abs(structure.startBeat), 0, structure.markers.Length - 1);
        int endIndex = Math.Clamp(structure.markers.Length - 1, 0, structure.markers.Length - 1);

        double startTime = structure.markers[startIndex] / 48d / 1000d;
        double endTime = structure.markers[endIndex] / 48d / 1000d;
        return Math.Max(0, endTime - startTime);
    }

    private static string ConvertColor(float[] rgba)
    {
        if (rgba == null || rgba.Length < 4)
            return "#FFFFFFFF";

        int a = (int)(rgba[0] * 255);
        int r = (int)(rgba[1] * 255);
        int g = (int)(rgba[2] * 255);
        int b = (int)(rgba[3] * 255);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }
}