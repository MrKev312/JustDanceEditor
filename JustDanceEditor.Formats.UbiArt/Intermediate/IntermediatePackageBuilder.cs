using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using JDIKaraokeClip = JustDanceEditor.Formats.JDI.Timelines.KaraokeClip;
using UbiArtKaraokeClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.KaraokeClip;
using JDIPictogramClip = JustDanceEditor.Formats.JDI.Timelines.PictogramClip;
using UbiArtPictogramClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.PictogramClip;
using JDIGoldEffectClip = JustDanceEditor.Formats.JDI.Timelines.GoldEffectClip;
using UbiArtGoldEffectClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.GoldEffectClip;
using JDIHideUserInterfaceClip = JustDanceEditor.Formats.JDI.Timelines.HideUserInterfaceClip;
using UbiArtHideUserInterfaceClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.HideUserInterfaceClip;
using JDIVibrationClip = JustDanceEditor.Formats.JDI.Timelines.VibrationClip;
using UbiArtVibrationClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.VibrationClip;

using SixLabors.ImageSharp.PixelFormats;

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
            List<MoveTimeline> coachTimelines,
            List<MoveTimeline> fullBodyTimelines,
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
            SongID = Guid.NewGuid(),
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
            Markers = [.. structure.markers]
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

    private static Timeline<JDIKaraokeClip> BuildLyricsDocument(ConversionContext context)
    {
        Timeline<JDIKaraokeClip> document = new();

        foreach (UbiArtKaraokeClip clip in context.SongData.Clips.OfType<UbiArtKaraokeClip>().OrderBy(c => c.StartTime))
        {
            JDIKaraokeClip entry = new()
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

    private static Timeline<JDIPictogramClip> BuildPictogramDocument(ConversionContext context)
    {
        Timeline<JDIPictogramClip> document = new();

        foreach (UbiArtPictogramClip clip in context.SongData.Clips.OfType<UbiArtPictogramClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new JDIPictogramClip
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

    private static Timeline<JDIGoldEffectClip> BuildGoldEffectDocument(ConversionContext context)
    {
        Timeline<JDIGoldEffectClip> document = new();

        foreach (UbiArtGoldEffectClip clip in context.SongData.Clips.OfType<UbiArtGoldEffectClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new JDIGoldEffectClip
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

    private static Timeline<JDIHideUserInterfaceClip> BuildHideUserInterfaceDocument(ConversionContext context)
    {
        Timeline<JDIHideUserInterfaceClip> document = new();

        foreach (UbiArtHideUserInterfaceClip clip in context.SongData.Clips.OfType<UbiArtHideUserInterfaceClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new JDIHideUserInterfaceClip
            {
                Id = clip.Id,
                IsActive = clip.IsActive > 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            });
        }

        return document;
    }

    private static Timeline<JDIVibrationClip> BuildVibrationDocument(ConversionContext context)
    {
        Timeline<JDIVibrationClip> document = new();

        foreach (UbiArtVibrationClip clip in context.SongData.Clips.OfType<UbiArtVibrationClip>().OrderBy(c => c.StartTime))
        {
            document.Clips.Add(new JDIVibrationClip
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
        List<MoveTimeline> HandTracking,
        List<MoveTimeline> FullBodyTracking,
        Dictionary<string, CoachMoveDefinition> HandMoves,
        Dictionary<string, CoachMoveDefinition> FullBodyMoves) BuildCoachTimelines(ConversionContext context, TimelineMath timelineMath)
    {
        Dictionary<int, MoveTimeline> handTimelines = [];
        Dictionary<int, MoveTimeline> fullBodyTimelines = [];
        Dictionary<string, CoachMoveDefinition> handMoves = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CoachMoveDefinition> fullBodyMoves = new(StringComparer.OrdinalIgnoreCase);

        foreach (MotionClip clip in context.SongData.Clips.OfType<MotionClip>())
        {
            if (!clip.ClassifierPath.EndsWith(".msm", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isFullBody = clip.MoveType == 1;
            Dictionary<int, MoveTimeline> target = isFullBody ? fullBodyTimelines : handTimelines;
            Dictionary<string, CoachMoveDefinition> moveCatalog = isFullBody ? fullBodyMoves : handMoves;

            if (!target.TryGetValue(clip.CoachId, out MoveTimeline? timeline))
            {
                timeline = new MoveTimeline { CoachId = clip.CoachId };
                target[clip.CoachId] = timeline;
            }

            string moveId = Path.GetFileNameWithoutExtension(clip.ClassifierPath).ToLowerInvariant();

            byte r = (byte)(clip.Color[1] * 255f);
            byte g = (byte)(clip.Color[2] * 255f);
            byte b = (byte)(clip.Color[3] * 255f);

            timeline.Clips.Add(new MoveClip
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                MoveId = moveId,
                IsGoldMove = clip.GoldMove == 1
            });

            AddOrUpdateMoveDefinition(
                moveCatalog,
                $"#{r:X2}{g:X2}{b:X2}",
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
        string color,
        string moveId,
        int duration,
        CoachMoveType moveType)
    {
        if (!catalog.TryGetValue(moveId, out CoachMoveDefinition? definition))
        {
            catalog[moveId] = new CoachMoveDefinition
            {
                Color = color,
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