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
            actors.Add(EmbeddedSubSceneContent(package, mapNameLower, suffix));
        }

        return new LegacySceneFile(0x0004905D, actors, new LegacyMainSceneFooter(), scenes.Length);
    }

    public object GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0004905D,
            AudioSceneActors(package, mapName, mapNameLower));
    }

    public object GenerateTimelineScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        if (EngineVersion == UbiArtEngineVersion.JD2014)
        {
            return new LegacySceneFile(
                0,
                [
                    new LegacyJd2014TimelineSceneActor(GenerateJd2014TimelineActorName(package), mapNameLower, Paths)
                ]);
        }

        return new LegacySceneFile(
            0x0003C5B6,
            [
                TimelineActor($"{mapName}_tml_dance", $"{mapNameLower}_tml_dance.tpl", mapNameLower, false),
                TimelineActor($"{mapName}_tml_karaoke", $"{mapNameLower}_tml_karaoke.tpl", mapNameLower, true)
            ]);
    }

    public object GenerateCinematicsScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0003C5B6,
            [
                ComponentActor(
                $"{mapName}_MainSequence",
                S(0, 1.0f, 1.0f, 0),
                S(0, 0, 0, 0, 0),
                $"{mapNameLower}_mainsequence.tpl",
                Paths.MapSubFolder(mapNameLower, "cinematics"),
                    S(0, 0, 1, 0x677B269Bu, Z(16)))
            ]);
    }

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
            actors.Select(actor => MenuArtSceneActor(mapName, mapNameLower, actor.Suffix, actor.Bounds0, actor.Bounds1)));
    }

    public object GenerateAutodanceScene(IntermediateSongPackage package) =>
        GenerateSingleActorScene(package.Metadata.MapName, "autodance", "autodance");

    public object GenerateGraphScene(string mapName) => new LegacySceneFile(
        0x00026CD2,
        [
            ComponentActor(
                "Camera_JD_Dummy",
                S(10.0f, 1.0f, 1.0f, 0),
                S(0, 0, 0, 0, 0),
                "tpl_emptyactor.tpl",
                "enginedata/actortemplates/",
                Z(28))
        ]);

    public object GenerateVideoScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0003C5B6,
            [
                VideoScreenActor(mapNameLower),
                VideoOutputActor()
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

    private object EmbeddedSubSceneContent(IntermediateSongPackage package, string mapNameLower, string suffix)
    {
        string mapName = package.Metadata.MapName;
        int coachCount = package.Metadata.CoachCount;

        return suffix switch
        {
            "_AUDIO" => new LegacySceneFile(
                0x0004905D,
                AudioSceneActors(package, mapName, mapNameLower)),
            "_CINE" => new LegacySceneFile(
                0x0004905D,
                [
                ComponentActor(
                $"{mapName}_MainSequence",
                S(0, 1.0f, 1.0f, 0),
                S(0, 0, 0, 0, 0),
                $"{mapNameLower}_mainsequence.tpl",
                Paths.MapSubFolder(mapNameLower, "cinematics"),
                S(2, 0, 1, 0x677B269Bu, Z(16)))
                ]),
            "_GRAPH" => new LegacySceneFile(
                0x0004905D,
                [
                ComponentActor(
                "Camera_JD_Dummy",
                S(0, 1.0f, 1.0f, 0),
                S(10.0f, 1.0f, 1.0f, 0, Z(3)),
                "tpl_emptyactor.tpl",
                "enginedata/actortemplates/",
                S(2, 0, Z(21)))
                ]),
            "_TML" => new LegacySceneFile(
                0x0004905D,
                EngineVersion == UbiArtEngineVersion.JD2014
                    ? [new LegacyJd2014TimelineSceneActor(GenerateJd2014TimelineActorName(package), mapNameLower, Paths)]
                    : [
                        TimelineActor($"{mapName}_tml_dance", $"{mapNameLower}_tml_dance.tpl", mapNameLower, false, 2),
                    TimelineActor($"{mapName}_tml_karaoke", $"{mapNameLower}_tml_karaoke.tpl", mapNameLower, true, 2)
                    ]),
            "_VIDEO" => new LegacySceneFile(
                0x0004905D,
                [
                VideoScreenActor(mapNameLower, true),
            VideoOutputActor(true)
                ]),
            "_menuart" => GenerateEmbeddedMenuArtScene(mapName, mapNameLower, coachCount),
            _ => S()
        };
    }

    private object GenerateEmbeddedMenuArtScene(string mapName, string mapNameLower, int coachCount)
    {
        List<object> actors = [];

        if (EngineVersion != UbiArtEngineVersion.JD2014)
            actors.Add(CoverActor($"{mapName}_cover_generic", mapNameLower, $"{mapNameLower}_cover_generic.tga", CoverPreData(), null, false));

        actors.Add(CoverActor($"{mapName}_cover_albumcoach", mapNameLower, $"{mapNameLower}_cover_albumcoach.tga", CoverPreData(), null, false));
        actors.Add(CoverActor($"{mapName}_cover_albumbkg", mapNameLower, $"{mapNameLower}_cover_albumbkg.tga", CoverPreData(), null, false));

        if (Platform == UbiArtPlatform.Revolution && EngineVersion != UbiArtEngineVersion.JD2014)
            actors.Add(CoverActor($"{mapName}_map_bkg", mapNameLower, $"{mapNameLower}_map_bkg.tga", MapBackgroundPreData(), MapBackgroundPostData(), false));

        for (int coachIndex = 1; coachIndex <= Math.Max(1, coachCount); coachIndex++)
        {
            actors.Add(CoverActor($"{mapName}_coach_{coachIndex}", mapNameLower, $"{mapNameLower}_coach_{coachIndex}.tga", CoachPreData(), CoachPostData(), true));
        }

        return new LegacySceneFile(0x0004905D, actors);
    }

    private static string GenerateJd2014TimelineActorName(IntermediateSongPackage package)
    {
        int pictogramCount = package.Pictograms.Clips.Count;
        int handMotionCount = package.CoachTimelines.Sum(timeline => timeline.Clips.Count);
        int fullBodyMotionCount = package.FullBodyCoachTimelines.Sum(timeline => timeline.Clips.Count);
        int lyricCount = package.Lyrics.Clips.Count;

        return $"timeline: {package.Metadata.MapName} ({pictogramCount} P ; {handMotionCount}/{fullBodyMotionCount} M ; {lyricCount} L)";
    }

    private IReadOnlyList<object> AudioSceneActors(IntermediateSongPackage package, string mapName, string mapNameLower)
    {
        List<object> actors =
        [
            MusicTrackActor(mapNameLower)
        ];

        if (EngineVersion != UbiArtEngineVersion.JD2014 && HasSoundSequence(package))
            actors.Add(EmbeddedSubScene($"{mapName}_sequence", $"{mapNameLower}_sequence.tpl", Paths.MapSubFolder(mapNameLower, "audio")));

        return actors;
    }

    private static bool HasSoundSequence(IntermediateSongPackage package)
    {
        bool hasIntroAmbience = package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1;
        return hasIntroAmbience ||
               package.Vibrations.Clips.Count > 0 ||
               package.HideUserInterface.Clips.Count > 0;
    }

    private object MusicTrackActor(string mapNameLower) => ComponentActor(
        "MusicTrack",
        S(0, 1.0f, 1.0f, 0),
        S(F(0x3F901F86u), F(0xBED6581Du), 0, 0, 0),
        UsesLegacyConvertedData ? $"{mapNameLower}_musictrack.main_legacy.tpl" : $"{mapNameLower}_musictrack.tpl",
        UsesLegacyConvertedData ? $"cache/legacyconverteddata/{mapNameLower}/audio/" : Paths.MapSubFolder(mapNameLower, "audio"),
        S(2, 0, 1, 0x7A7C235Bu, 0x97CA628Bu, 0x358637BDu));

    private object TimelineActor(string name, string tpl, string mapNameLower, bool karaoke, int tailPrefix = 0) => ComponentActor(
        name,
        S(0x358637BDu, 1.0f, 1.0f, 0),
        S(0xBF9430D3u, F(0x3BC9C90Cu), 0, 0, 0),
        tpl,
        Paths.MapSubFolder(mapNameLower, "timeline"),
        karaoke
            ? S(tailPrefix, 0, 1, 0x231F27DEu, Z(16))
            : S(tailPrefix, 0, 1, 0x231F27DEu));

    private static object ComponentActor(
        string name,
        object preData,
        object postData,
        string tpl,
        string path,
        object tail) => new LegacyComponentActor(name, preData, postData, tpl, path, tail);

    private static object EmbeddedSubScene(string name, string tpl, string path) =>
        new LegacyEmbeddedSubSceneActor(name, tpl, path);

    private object VideoScreenActor(string mapNameLower, bool embedded = false) => new LegacyVideoScreenActor(mapNameLower, Paths, embedded);

    private object VideoOutputActor(bool embedded = false) => new LegacyVideoOutputActor(Paths, embedded);

    private object MenuArtSceneActor(string mapName, string mapNameLower, string suffix, uint bounds0, uint bounds1) =>
        new LegacyMenuArtSceneActor(mapName, mapNameLower, suffix, bounds0, bounds1, Paths);

    private object CoverActor(
        string name,
        string mapNameLower,
        string textureFile,
        object preData,
        object? specificPostData,
        bool coachFooter) => new LegacyCoverActor(
            name,
            mapNameLower,
            textureFile,
            preData,
            specificPostData ?? S(0x43850B35u, 0x4345A145u, 0, 0, 0, uint.MaxValue, 0),
            coachFooter ? S(0, 0, 0x00060000, 0x00010000, 0x00020000, 0, 0, 0, Z(2)) : S(0, 0, 1),
            Paths);

    private static LegacyBinarySequence CoverPreData() => S(0, 0.3f, 0.3f, 0);
    private static LegacyBinarySequence MapBackgroundPreData() => S(0, 256.0f, 128.0f, 0);
    private static LegacyBinarySequence CoachPreData() => S(0, F(0x3E949689u), F(0x3E949689u), 0);

    private static LegacyBinarySequence MapBackgroundPostData() => S(
        0x44B9ED1Fu,
        350.0f,
        0,
        0,
        0,
        uint.MaxValue,
        0);

    private static LegacyBinarySequence CoachPostData() => S(
        0x4354C8D5u,
        0x4425EB88u,
        0,
        0,
        0,
        uint.MaxValue,
        0);

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
        trackId > 0 && trackId <= uint.MaxValue ? (uint)trackId : fallback;
}