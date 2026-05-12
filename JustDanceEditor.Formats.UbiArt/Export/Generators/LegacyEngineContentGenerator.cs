using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

public class LegacyEngineContentGenerator(UbiArtEngineVersion EngineVersion, UbiArtPlatform Platform = UbiArtPlatform.Wii) : IEngineContentGenerator
{
    private static LegacyBinarySequence Seq(params object?[] fields) => LegacyBinary.Sequence(fields);
    private static LegacyBinarySequence S(params object?[] fields) => LegacyBinary.Sequence(fields);
    private static LegacyPadding Z(int length) => LegacyBinary.Padding(length);
    private static LegacyUbiArtPath P(string fileName, string folder) => LegacyBinary.Path(fileName, folder);
    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

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
                    TrackId = 0,
                    StartTime = clip.StartTime,
                    Duration = move.Duration,
                    ClassifierPath = P($"{clip.MoveId}.msm", $"world/maps/{mapNameLower}/timeline/moves/"),
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
                PictoPath = P($"{clip.PictogramId}.png", $"world/maps/{mapNameLower}/timeline/pictos/")
            });
        }

        foreach (GoldEffectClip clip in package.GoldEffects.Clips)
        {
            clips.Add(new LegacyGoldEffectClip
            {
                Id = (uint)clip.Id,
                TrackId = 1111,
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
                SoundSetPath = P($"amb_{mapNameLower}_intro.tpl", $"world/maps/{mapNameLower}/audio/amb/")
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
            (float)package.TimelineStructure.VideoStartOffset);
    }

    public object GenerateTapeCaseTpl(string mapName, string tapeType) => new LegacyTapeCaseFile(mapName, tapeType);

    public object GenerateSequenceTpl() => new LegacySequenceTplFile();

    public object GenerateSoundTape(string mapName) => new LegacySoundTapeFile(mapName);

    public object GenerateAmbTpl(string mapName) => new LegacyAmbTplFile(mapName);

    public object GenerateMainSequenceTpl(string mapName) => new LegacyMainSequenceTplFile(mapName);

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
            actors.Add(EmbeddedSubSceneContent(mapName, mapNameLower, suffix));
        }

        return new LegacySceneFile(0x0004905D, actors, new LegacyMainSceneFooter(), scenes.Length);
    }

    public object GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return new LegacySceneFile(
            0x0004905D,
            [
                MusicTrackActor(mapNameLower),
                EmbeddedSubScene($"{mapName}_sequence", $"{mapNameLower}_sequence.tpl", $"world/maps/{mapNameLower}/audio/")
            ]);
    }

    public object GenerateTimelineScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

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
                $"world/maps/{mapNameLower}/cinematics/",
                    S(0, 0, 1, 0x677B269Bu, Z(16)))
            ]);
    }

    public object GenerateMenuArtScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        (string Suffix, uint Bounds0, uint Bounds1)[] actors =
        [
            ("cover_generic", 0x43850B35u, 0x4345A145u),
            ("cover_online", 0xC3160000u, 0),
            ("cover_albumcoach", 0x443886CEu, 0x43B3CE57u),
            ("cover_albumbkg", 0x44857F1Cu, 0x4349FC80u),
            ("coach_4", 0x44031864u, 0x4427B51Cu),
            ("coach_1", 0x4354C8D5u, 0x4425EB88u),
            ("coach_2", 0x44031864u, 0x4427B51Cu),
            ("coach_3", 0x44031864u, 0x4427B51Cu)
        ];

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

    public object GenerateVideoPlayerActor(string mapName, bool isPreview) => new LegacyVideoPlayerActorFile(mapName);

    public object GenerateMpd() => new LegacyMpdFile();

    public object GenerateAutodanceActor(string mapName) => new LegacyAutodanceActorFile(mapName);

    public object GenerateMenuArtActor(string textureName, string mapName) => new LegacyMenuArtActorFile(textureName, mapName);

    private static object GenerateSingleActorScene(string mapName, string suffix, string folder, string extension = "act") =>
        new LegacySingleActorSceneFile(mapName, suffix, folder, extension);

    private static object SubSceneDefinition(string mapName, string mapNameLower, string suffix, string folder, int endValue) =>
        new LegacySubSceneDefinitionActor(mapName, mapNameLower, suffix, folder, endValue);

    private static object SongDescSceneActor(string mapName, string mapNameLower) =>
        new LegacySongDescSceneActor(mapName, mapNameLower);

    private object EmbeddedSubSceneContent(string mapName, string mapNameLower, string suffix) => suffix switch
    {
        "_AUDIO" => new LegacySceneFile(
            0x0004905D,
            [
            MusicTrackActor(mapNameLower),
            EmbeddedSubScene($"{mapName}_sequence", $"{mapNameLower}_sequence.tpl", $"world/maps/{mapNameLower}/audio/")
            ]),
        "_CINE" => new LegacySceneFile(
            0x0004905D,
            [
            ComponentActor(
                $"{mapName}_MainSequence",
                S(0, 1.0f, 1.0f, 0),
                S(0, 0, 0, 0, 0),
                $"{mapNameLower}_mainsequence.tpl",
                $"world/maps/{mapNameLower}/cinematics/",
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
            [
            TimelineActor($"{mapName}_tml_dance", $"{mapNameLower}_tml_dance.tpl", mapNameLower, false, 2),
            TimelineActor($"{mapName}_tml_karaoke", $"{mapNameLower}_tml_karaoke.tpl", mapNameLower, true, 2)
            ]),
        "_VIDEO" => new LegacySceneFile(
            0x0004905D,
            [
            VideoScreenActor(mapNameLower, true),
            VideoOutputActor(true)
            ]),
        "_menuart" => GenerateEmbeddedMenuArtScene(mapName, mapNameLower),
        _ => S()
    };

    private object GenerateEmbeddedMenuArtScene(string mapName, string mapNameLower)
    {
        List<object> actors =
        [
            CoverActor($"{mapName}_cover_generic", mapNameLower, $"{mapNameLower}_cover_generic.tga", CoverPreData(), null, false),
            CoverActor($"{mapName}_cover_online_Kids", mapNameLower, $"{mapNameLower}_cover_online_kids.tga", CoverPreData(), null, false),
            CoverActor($"{mapName}_cover_online", mapNameLower, $"{mapNameLower}_cover_online.tga", CoverPreData(), null, false),
            CoverActor($"{mapName}_cover_albumcoach", mapNameLower, $"{mapNameLower}_cover_albumcoach.tga", CoverPreData(), null, false),
            CoverActor($"{mapName}_cover_albumbkg", mapNameLower, $"{mapNameLower}_cover_albumbkg.tga", CoverPreData(), null, false)
        ];

        if (Platform == UbiArtPlatform.Wii)
            actors.Add(CoverActor($"{mapName}_map_bkg", mapNameLower, $"{mapNameLower}_map_bkg.tga", MapBackgroundPreData(), MapBackgroundPostData(), false));

        actors.Add(CoverActor($"{mapName}_coach_1", mapNameLower, $"{mapNameLower}_coach_1.tga", CoachPreData(), CoachPostData(), true));

        return new LegacySceneFile(0x0004905D, actors);
    }

    private static object MusicTrackActor(string mapNameLower) => ComponentActor(
        "MusicTrack",
        S(0, 1.0f, 1.0f, 0),
        S(F(0x3F901F86u), F(0xBED6581Du), 0, 0, 0),
        $"{mapNameLower}_musictrack.main_legacy.tpl",
        $"cache/legacyconverteddata/{mapNameLower}/audio/",
        S(2, 0, 1, 0x7A7C235Bu, 0x97CA628Bu, 0x358637BDu));

    private static object TimelineActor(string name, string tpl, string mapNameLower, bool karaoke, int tailPrefix = 0) => ComponentActor(
        name,
        S(0x358637BDu, 1.0f, 1.0f, 0),
        S(0xBF9430D3u, F(0x3BC9C90Cu), 0, 0, 0),
        tpl,
        $"world/maps/{mapNameLower}/timeline/",
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

    private static object VideoScreenActor(string mapNameLower, bool embedded = false) => new LegacyVideoScreenActor(mapNameLower, embedded);

    private static object VideoOutputActor(bool embedded = false) => new LegacyVideoOutputActor(embedded);

    private static object MenuArtSceneActor(string mapName, string mapNameLower, string suffix, uint bounds0, uint bounds1) =>
        new LegacyMenuArtSceneActor(mapName, mapNameLower, suffix, bounds0, bounds1);

    private static object CoverActor(
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
            coachFooter ? S(0, 0, 0x00060000, 0x00010000, 0x00020000, 0, 0, 0, Z(2)) : S(0, 0, 1));

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
}
