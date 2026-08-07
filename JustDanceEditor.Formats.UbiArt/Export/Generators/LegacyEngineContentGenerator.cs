using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

public class LegacyEngineContentGenerator(UbiArtEngineVersion EngineVersion, UbiArtPlatform Platform = UbiArtPlatform.Revolution) : IEngineContentGenerator
{
    private static LegacyBinarySequence Seq(params object?[] fields) => LegacyBinary.Sequence(fields);
    private static LegacyBinarySequence S(params object?[] fields) => LegacyBinary.Sequence(fields);
    private static LegacyPadding Z(int length) => LegacyBinary.Padding(length);
    private static LegacyUbiArtPath P(string fileName, string folder) => LegacyBinary.Path(fileName, folder);
    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    private LegacyPathContext Paths { get; } = CreatePathContext(EngineVersion);
    private bool UsesLegacyConvertedData => EngineVersion >= UbiArtEngineVersion.JD2016;
    private LegacySceneContentBuilder SceneBuilder => new(EngineVersion, Platform, Paths);

    private static LegacyPathContext CreatePathContext(UbiArtEngineVersion engineVersion)
    {
        string mapRoot = engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => "world/jd5",
            UbiArtEngineVersion.JD2015 => "world/jd2015",
            _ => "world/maps"
        };

        string commonRoot = engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => "world/jd5/_common",
            UbiArtEngineVersion.JD2015 => "world/jd2015/_common",
            _ => "world/_common"
        };

        return new LegacyPathContext(mapRoot, commonRoot);
    }

    public object GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<LegacyTapeClip> clips = [];

        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                clips.Add(new LegacyMotionClip
                {
                    Id = (uint)clip.Id,
                    TrackId = ToLegacyTrackId(timeline.TrackId, 0),
                    StartTime = clip.StartTime,
                    Duration = move.Duration,
                    ClassifierPath = P($"{clip.MoveId}.msm", Paths.MapSubFolder(mapNameLower, "timeline/moves")),
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    CoachId = timeline.CoachId,
                    MoveType = (int)move.MoveType,
                    Color = ConvertColorToAbgr(move.Color)
                });
            }
        }

        foreach (MoveTimeline timeline in package.FullBodyCoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.FullBodyCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                clips.Add(new LegacyMotionClip
                {
                    Id = (uint)clip.Id,
                    TrackId = ToLegacyTrackId(timeline.TrackId, 0),
                    StartTime = clip.StartTime,
                    Duration = move.Duration,
                    ClassifierPath = P($"{clip.MoveId}.gesture", Paths.MapSubFolder(mapNameLower, "timeline/moves")),
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    CoachId = timeline.CoachId,
                    MoveType = (int)move.MoveType,
                    Color = ConvertColorToAbgr(move.Color)
                });
            }
        }

        foreach (PictogramClip clip in package.Pictograms.Clips)
        {
            clips.Add(new LegacyPictogramClip
            {
                Id = (uint)clip.Id,
                TrackId = 1111,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                PictoPath = P($"{clip.PictogramId}.png", Paths.MapSubFolder(mapNameLower, "timeline/pictos"))
            });
        }

        foreach (GoldEffectClip clip in package.GoldEffects.Clips)
        {
            clips.Add(new LegacyGoldEffectClip
            {
                Id = (uint)clip.Id,
                TrackId = ToLegacyTrackId(clip.TrackId, 1111),
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                EffectType = clip.EffectType
            });
        }

        return new LegacyTapeFile(package.Metadata.MapName, EngineVersion, clips.OrderBy(x => x.StartTime));
    }

    public object GenerateKaraokeTape(IntermediateSongPackage package)
    {
        IReadOnlyList<LegacyTapeClip> clips = [.. package.Lyrics.Clips
            .OrderBy(c => c.StartTime)
            .Select(clip => new LegacyKaraokeClip
            {
                Id = (uint)clip.Id,
                TrackId = 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                Pitch = clip.Pitch > 0 ? clip.Pitch : 8.175798f,
                Lyrics = clip.Lyrics ?? string.Empty,
                IsEndOfLine = clip.IsEndOfLine ? 1 : 0,
                ContentType = clip.ContentType,
                StartTimeTolerance = clip.Tolerances?.StartTimeTolerance ?? 4,
                EndTimeTolerance = clip.Tolerances?.EndTimeTolerance ?? 4,
                SemitoneTolerance = (float)(clip.Tolerances?.SemitoneTolerance ?? 5)
            })];

        return new LegacyTapeFile(package.Metadata.MapName, EngineVersion, clips);
    }

    public object GenerateMainSequenceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<LegacyTapeClip> clips = [];

        bool hasIntroAmbience = package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1;
        if (hasIntroAmbience)
        {
            clips.Add(new LegacySoundSetClip
            {
                Id = 67890,
                TrackId = 2222,
                StartTime = package.TimelineStructure.StartBeat * 24,
                Duration = 1200,
                SoundSetPath = P($"amb_{mapNameLower}_intro.tpl", Paths.MapSubFolder(mapNameLower, "audio/amb"))
            });
        }

        uint trackId = 1111;
        uint clipIdCounter = 12345;
        foreach (VibrationClip clip in package.Vibrations.Clips)
        {
            clips.Add(new LegacyVibrationClip
            {
                Id = clip.Id != 0 ? (uint)clip.Id : clipIdCounter++,
                TrackId = clip.TrackId != 0 ? (uint)clip.TrackId : trackId++,
                IsActive = 1,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            });
        }

        foreach (HideUserInterfaceClip clip in package.HideUserInterface.Clips)
        {
            clips.Add(new LegacyHideUserInterfaceClip(EngineVersion)
            {
                Id = clip.Id != 0 ? (uint)clip.Id : clipIdCounter++,
                TrackId = trackId++,
                IsActive = clip.IsActive ? 1 : 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            });
        }

        return new LegacyTapeFile(package.Metadata.MapName, EngineVersion, clips);
    }

    public object GenerateAutodanceTape(IntermediateSongPackage package) => Seq();

    public object GenerateSongDesc(IntermediateSongPackage package)
    {
        LegacyAbgrColor lyricColor = ConvertColorToAbgr(package.Metadata.LyricsColor);

        if (EngineVersion == UbiArtEngineVersion.JD2014)
        {
            return new LegacyJd2014SongDescFile(
                package.Metadata.MapName,
                package.Metadata.Artist,
                package.Metadata.Title,
                package.Metadata.CoachCount,
                package.Metadata.Difficulty,
                package.TimelineStructure.PreviewEntryBeat,
                package.TimelineStructure.PreviewLoopStartBeat,
                package.TimelineStructure.PreviewLoopEndBeat,
                lyricColor);
        }

        return new LegacySongDescFile(
            package.Metadata.MapName,
            EngineVersion,
            package.Metadata.OriginalJDVersion,
            package.Metadata.Artist,
            package.Metadata.Title,
            package.Metadata.CoachCount,
            package.Metadata.Difficulty,
            package.TimelineStructure.PreviewEntryBeat,
            package.TimelineStructure.PreviewLoopStartBeat,
            package.TimelineStructure.PreviewLoopEndBeat,
            lyricColor);
    }

    public object GenerateMusicTrack(IntermediateSongPackage package)
    {
        IReadOnlyList<LegacySignatureMarker> signatures = [.. package.TimelineStructure.Signatures
            .Select(sig => new LegacySignatureMarker((int)sig.Marker, sig.Beats))];

        IReadOnlyList<LegacySectionMarker> sections = [.. package.TimelineStructure.Sections
            .Select(section => new LegacySectionMarker(
                (int)section.StartBeat,
                (int)section.SectionType,
                section.Comment ?? string.Empty))];

        return new LegacyMusicTrackFile(
            package.Metadata.MapName,
            EngineVersion,
            package.TimelineStructure.Markers,
            signatures,
            sections,
            package.TimelineStructure.StartBeat,
            (uint)package.TimelineStructure.EndBeat,
            (float)package.TimelineStructure.VideoStartOffset,
            Paths);
    }

    public object GenerateTapeCaseTpl(string mapName, string tapeType) => new LegacyTapeCaseFile(mapName, tapeType, Paths);

    public object GenerateJd2014Timeline(IntermediateSongPackage package) =>
        LegacyJd2014TimelineSerializer.Serialize(package, Paths);

    public object GenerateJd2014TimelineActor(string mapName) => new LegacyJd2014TimelineActorFile(mapName, Paths);

    public object GenerateSequenceTpl() => new LegacySequenceTplFile();

    public object GenerateSoundTape(string mapName) => new LegacySoundTapeFile(mapName);

    public object GenerateAmbTpl(string mapName) => new LegacyAmbTplFile(mapName, Paths);

    public object GenerateMainSequenceTpl(string mapName) => new LegacyMainSequenceTplFile(mapName, Paths);

    public object GenerateSgs() => new LegacySgsFile();

    public object GenerateGenericActor(string className, string luaPath) => new LegacyGenericActorFile(luaPath);

    public object GenerateMainScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        (string Suffix, string Folder, int EndValue, bool IsSongDesc)[] scenes =
        [
            ("_AUDIO", "audio", 2, false),
            ("_CINE", "cinematics", 2, false),
            ("_GRAPH", "graph", 2, false),
            ("_TML", "timeline", 2, false),
            ("_VIDEO", "videoscoach", 2, false),
            ("SongDesc", string.Empty, 2, true),
            ("_menuart", "menuart", 3, false)
        ];

        List<object?> actors = [];
        foreach ((string suffix, string folder, int endValue, bool isSongDesc) in scenes)
        {
            if (isSongDesc)
            {
                actors.Add(SongDescSceneActor(mapName, mapNameLower));
                continue;
            }

            actors.Add(SubSceneDefinition(mapName, mapNameLower, suffix, folder, endValue));
            actors.Add(SceneBuilder.EmbeddedSubSceneContent(package, mapNameLower, suffix));
        }

        return new LegacySceneFile(0x0004905D, actors, new LegacyMainSceneFooter(), scenes.Length);
    }

    public object GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0004905D,
            SceneBuilder.AudioSceneActors(package, mapName, mapNameLower));
    }

    public object GenerateTimelineScene(IntermediateSongPackage package) => SceneBuilder.TimelineScene(package);

    public object GenerateCinematicsScene(IntermediateSongPackage package) => SceneBuilder.CinematicsScene(package.Metadata.MapName);

    public object GenerateMenuArtScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        List<(string Suffix, uint Bounds0, uint Bounds1)> actors = [];

        if (EngineVersion != UbiArtEngineVersion.JD2014)
            actors.Add(("cover_generic", 0x43850B35u, 0x4345A145u));

        actors.Add(("cover_albumcoach", 0x443886CEu, 0x43B3CE57u));
        actors.Add(("cover_albumbkg", 0x44857F1Cu, 0x4349FC80u));

        for (int coachIndex = 1; coachIndex <= Math.Max(1, package.Metadata.CoachCount); coachIndex++)
        {
            actors.Add(coachIndex == 1
                ? ($"coach_{coachIndex}", 0x4354C8D5u, 0x4425EB88u)
                : ($"coach_{coachIndex}", 0x44031864u, 0x4427B51Cu));
        }

        return new LegacySceneFile(
            0x0003C5B6,
            actors.Select(actor => SceneBuilder.MenuArtSceneActor(mapName, mapNameLower, actor.Suffix, actor.Bounds0, actor.Bounds1)));
    }

    public object GenerateAutodanceScene(IntermediateSongPackage package) =>
        GenerateSingleActorScene(package.Metadata.MapName, "autodance", "autodance");

    public object GenerateGraphScene(string mapName) => SceneBuilder.GraphScene();

    public object GenerateVideoScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0003C5B6,
            [
                SceneBuilder.VideoScreenActor(mapNameLower),
                SceneBuilder.VideoOutputActor()
            ]);
    }

    public object GenerateVideoMapPreviewScene(string mapName) => GenerateSingleActorScene(mapName, "videomappreview", "video");

    public object GenerateVideoPlayerActor(string mapName, bool isPreview) => new LegacyVideoPlayerActorFile(mapName, Paths);

    public object GenerateMpd() => new LegacyMpdFile();

    public object GenerateAutodanceActor(string mapName) => new LegacyAutodanceActorFile(mapName, Paths);

    public object GenerateMenuArtActor(string textureName, string mapName) => new LegacyMenuArtActorFile(textureName, mapName, Paths);

    private object GenerateSingleActorScene(string mapName, string suffix, string folder, string extension = "act") =>
        new LegacySingleActorSceneFile(mapName, suffix, folder, extension, Paths);

    private object SubSceneDefinition(string mapName, string mapNameLower, string suffix, string folder, int endValue) =>
        new LegacySubSceneDefinitionActor(mapName, mapNameLower, suffix, folder, endValue, Paths);

    private object SongDescSceneActor(string mapName, string mapNameLower) =>
        new LegacySongDescSceneActor(mapName, mapNameLower, Paths, EngineVersion >= UbiArtEngineVersion.JD2016);

    private static LegacyAbgrColor ConvertColorToAbgr(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return LegacyAbgrColor.White;

        try
        {
            string hex = hexColor.TrimStart('#');
            if (hex.Length == 6)
                hex += "FF";
            if (hex.Length != 8)
                return LegacyAbgrColor.White;

            return new LegacyAbgrColor(
                int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0f);
        }
        catch
        {
            return LegacyAbgrColor.White;
        }
    }

    private static uint ToLegacyTrackId(long trackId, uint fallback) =>
        trackId is > 0 and <= uint.MaxValue ? (uint)trackId : fallback;
}
