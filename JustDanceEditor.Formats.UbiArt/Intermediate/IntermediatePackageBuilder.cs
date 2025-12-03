using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.JDI.Manifests;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using System.Text.Json;

using IntermediateKaraokeClip = JustDanceEditor.Formats.JDI.Timelines.KaraokeClip;
using UbiArtKaraokeClip = JustDanceEditor.Formats.UbiArt.Tapes.Clips.KaraokeClip;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

internal static class IntermediatePackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

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
            AssetCatalog = BuildAssetCatalog(context),
            TimelineStructure = BuildTimelineStructure(context, structure, timelineMath),
            Lyrics = BuildLyricsDocument(context, timelineMath),
            Pictograms = BuildPictogramDocument(context, timelineMath),
            Events = BuildEventDocument(context, timelineMath),
            CoachTimelines = coachTimelines,
            FullBodyCoachTimelines = fullBodyTimelines,
            HandCoachMoves = handMoves,
            FullBodyCoachMoves = fullBodyMoves,
            Manifest = new IntermediatePackageManifest()
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
            SongId = context.SongID,
            MapName = info.MapName,
            ParentMapName = info.MapName,
            Title = info.Title,
            Artist = info.Artist,
            Credits = info.Credits,
            LyricsColor = lyricsColor,
            MapLengthSeconds = mapLengthSeconds,
            OriginalJdVersion = context.SongData.JDVersion,
            CoachCount = info.NumCoach,
            CoachNames = BuildCoachNames(info.NumCoach),
            Difficulty = info.Difficulty,
            SweatDifficulty = info.SweatDifficulty,
            Tags = info.Tags?.ToList() ?? [],
            Status = info.Status,
            HasSongTitleInCover = DetectSongTitleLogo(context),
            MojoValue = info.MojoValue,
            CountInProgression = info.CountInProgression
        };

        metadata.AdditionalMetadata["platformType"] = context.FileSystem.PlatformType;
        metadata.AdditionalMetadata["videoPreviewPath"] = info.VideoPreviewPath;

        return metadata;
    }

    private static IntermediateAssetCatalog BuildAssetCatalog(ConversionContext context)
    {
        IntermediateAssetCatalog catalog = new();
        Trackdata trackData = context.SongData.MusicTrack.COMPONENTS.FirstOrDefault()?.trackData ?? new();

        IntermediateAsset masterAudio = catalog.Add("audio/master");
        masterAudio.SourcePath = trackData.path;
        masterAudio.Attributes["format"] = "ogg";

        IntermediateAsset previewAudio = catalog.Add("audio/preview");
        previewAudio.GeneratedPath = context.FileSystem.OutputFolders.PreviewAudioFolder;
        previewAudio.Required = false;

        IntermediateAsset videoAsset = catalog.Add("video/background");
        videoAsset.Attributes["expectedFolder"] = context.FileSystem.OutputFolders.VideoFolder;
        videoAsset.Required = false;

        IntermediateAsset coverAsset = catalog.Add("image/cover");
        coverAsset.SourcePath = Path.Combine(context.FileSystem.InputFolders.MenuArtFolder, $"{context.SongData.Name}_cover.png");
        coverAsset.GeneratedPath = context.FileSystem.OutputFolders.CoverFolder;

        IntermediateAsset titleLogoAsset = catalog.Add("image/songTitleLogo");
        titleLogoAsset.SourcePath = Path.Combine(context.FileSystem.InputFolders.MapWorldFolder, "songTitleLogo");
        titleLogoAsset.GeneratedPath = context.FileSystem.OutputFolders.SongTitleLogoFolder;
        titleLogoAsset.Required = false;

        IntermediateAsset coachLargeAsset = catalog.Add("image/coachLarge");
        coachLargeAsset.GeneratedPath = context.FileSystem.OutputFolders.CoachesLargeFolder;

        IntermediateAsset pictogramAtlas = catalog.Add("atlas/pictograms");
        pictogramAtlas.GeneratedPath = context.FileSystem.OutputFolders.MapPackageFolder;

        IntermediateAsset motionScripts = catalog.Add("motion/msm");
        motionScripts.GeneratedPath = Path.Combine(context.FileSystem.OutputFolders.MapPackageFolder, "moves");
        motionScripts.Required = false;

        IntermediateAsset gestureScripts = catalog.Add("motion/gestures");
        gestureScripts.GeneratedPath = Path.Combine(context.FileSystem.OutputFolders.MapPackageFolder, "gestures");
        gestureScripts.Required = false;

        return catalog;
    }

    private static TimelineStructureDocument BuildTimelineStructure(ConversionContext context, Structure structure, TimelineMath timelineMath)
    {
        TimelineStructureDocument document = new()
        {
            TimeBaseMsPerBeat = timelineMath.EstimateMsPerBeat(),
            AudioStartOffset = context.SongData.GetSongStartTime(),
            VideoStartOffset = structure.videoStartTime,
            PreviewEntryBeat = structure.previewEntry,
            PreviewLoopStartBeat = structure.previewLoopStart,
            PreviewLoopEndBeat = structure.previewLoopEnd
        };

        foreach ((int index, int marker) in structure.markers.Select((marker, index) => (index, marker)))
        {
            document.Markers.Add(new TimelineMarker
            {
                BeatIndex = index,
                TimeMs = (int)Math.Round(marker / 48d)
            });
        }

        if (structure.signatures is { Length: > 0 })
        {
            foreach (Signature signature in structure.signatures)
            {
                document.Signatures.Add(new SignatureSegment
                {
                    StartBeat = signature.marker,
                    Numerator = signature.beats,
                    Denominator = 4
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
                    SectionType = section.sectionType.ToString(),
                    Comment = section.comment
                });
            }
        }

        document.TempoSegments.Add(new TempoSegment
        {
            StartBeat = structure.startBeat,
            BeatsPerMinute = timelineMath.EstimateBpm()
        });

        if (structure.useFadeStartBeat)
        {
            document.FadeIn = new FadeRegion
            {
                StartBeat = structure.fadeStartBeat,
                Duration = Math.Max(0, structure.fadeEndBeat - structure.fadeStartBeat),
                CurveType = structure.fadeInType == 0 ? "linear" : "custom"
            };
        }

        if (structure.useFadeEndBeat)
        {
            document.FadeOut = new FadeRegion
            {
                StartBeat = structure.fadeEndBeat,
                Duration = Math.Max(0, structure.endBeat - structure.fadeEndBeat),
                CurveType = structure.fadeOutType == 0 ? "linear" : "custom"
            };
        }

        return document;
    }

    private static LyricsTimelineDocument BuildLyricsDocument(ConversionContext context, TimelineMath timelineMath)
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

    private static PictogramTimelineDocument BuildPictogramDocument(ConversionContext context, TimelineMath timelineMath)
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

    private static EventTimelineDocument BuildEventDocument(ConversionContext context, TimelineMath timelineMath)
    {
        EventTimelineDocument document = new();

        IEnumerable<IClip> eventClips = context.SongData.Clips.Where(clip => clip is GoldEffectClip or HideUserInterfaceClip or GameplayEventClip or VibrationClip);

        foreach (IClip clip in eventClips.OrderBy(c => c.StartTime))
        {
            TimelineEvent timelineEvent = new()
            {
                Id = clip.Id,
                Type = clip.__class,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                Payload = JsonSerializer.SerializeToElement(clip, JsonOptions)
            };

            document.Events.Add(timelineEvent);
        }

        return document;
    }

    private static (
        List<CoachTimelineDocument> HandTracking,
        List<CoachTimelineDocument> FullBodyTracking,
        Dictionary<string, CoachMoveDefinition> HandMoves,
        Dictionary<string, CoachMoveDefinition> FullBodyMoves) BuildCoachTimelines(ConversionContext context, TimelineMath timelineMath)
    {
        Dictionary<int, CoachTimelineDocument> handTimelines = new();
        Dictionary<int, CoachTimelineDocument> fullBodyTimelines = new();
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

    private static string[] BuildCoachNames(int coachCount)
    {
        if (coachCount <= 0)
            return [];

        string[] names = new string[coachCount];
        for (int i = 0; i < coachCount; i++)
            names[i] = $"Coach {i + 1}";
        return names;
    }

    private static bool DetectSongTitleLogo(ConversionContext context)
    {
        string relative = Path.Combine(context.FileSystem.InputFolders.MapWorldFolder, "songTitleLogo");
        if (context.FileSystem.GetFolderPath(relative, out string? folder) && Directory.Exists(folder))
            return Directory.EnumerateFiles(folder).Any();
        return false;
    }
}
