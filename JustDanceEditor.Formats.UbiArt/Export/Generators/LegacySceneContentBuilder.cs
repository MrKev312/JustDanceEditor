using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal sealed class LegacySceneContentBuilder(
    UbiArtEngineVersion engineVersion,
    UbiArtPlatform platform,
    LegacyPathContext paths)
{
    private static LegacyBinarySequence S(params object?[] fields) => LegacyBinary.Sequence(fields);
    private static LegacyPadding Z(int length) => LegacyBinary.Padding(length);
    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    private bool UsesLegacyConvertedData => engineVersion >= UbiArtEngineVersion.JD2016;

    public object EmbeddedSubSceneContent(IntermediateSongPackage package, string mapNameLower, string suffix)
    {
        string mapName = package.Metadata.MapName;
        return suffix switch
        {
            "_AUDIO" => new LegacySceneFile(0x0004905D, AudioSceneActors(package, mapName, mapNameLower)),
            "_CINE" => new LegacySceneFile(0x0004905D,
            [
                ComponentActor(
                    $"{mapName}_MainSequence",
                    S(0, 1.0f, 1.0f, 0),
                    S(0, 0, 0, 0, 0),
                    $"{mapNameLower}_mainsequence.tpl",
                    paths.MapSubFolder(mapNameLower, "cinematics"),
                    S(2, 0, 1, 0x677B269Bu, Z(16)))
            ]),
            "_GRAPH" => new LegacySceneFile(0x0004905D,
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
                engineVersion == UbiArtEngineVersion.JD2014
                    ? [new LegacyJd2014TimelineSceneActor(GenerateJd2014TimelineActorName(package), mapNameLower, paths)]
                    :
                    [
                        TimelineActor($"{mapName}_tml_dance", $"{mapNameLower}_tml_dance.tpl", mapNameLower, false, 2),
                        TimelineActor($"{mapName}_tml_karaoke", $"{mapNameLower}_tml_karaoke.tpl", mapNameLower, true, 2)
                    ]),
            "_VIDEO" => new LegacySceneFile(0x0004905D,
            [
                VideoScreenActor(mapNameLower, true),
                VideoOutputActor(true)
            ]),
            "_menuart" => GenerateEmbeddedMenuArtScene(mapName, mapNameLower, package.Metadata.CoachCount),
            _ => S()
        };
    }

    public IReadOnlyList<object> AudioSceneActors(IntermediateSongPackage package, string mapName, string mapNameLower)
    {
        List<object> actors = [MusicTrackActor(mapNameLower)];
        if (engineVersion != UbiArtEngineVersion.JD2014 && HasSoundSequence(package))
            actors.Add(new LegacyEmbeddedSubSceneActor($"{mapName}_sequence", $"{mapNameLower}_sequence.tpl", paths.MapSubFolder(mapNameLower, "audio")));
        return actors;
    }

    public object TimelineScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        return new LegacySceneFile(
            engineVersion == UbiArtEngineVersion.JD2014 ? 0u : 0x0003C5B6u,
            engineVersion == UbiArtEngineVersion.JD2014
                ? [new LegacyJd2014TimelineSceneActor(GenerateJd2014TimelineActorName(package), mapNameLower, paths)]
                :
                [
                    TimelineActor($"{mapName}_tml_dance", $"{mapNameLower}_tml_dance.tpl", mapNameLower, false, 0),
                    TimelineActor($"{mapName}_tml_karaoke", $"{mapNameLower}_tml_karaoke.tpl", mapNameLower, true, 0)
                ]);
    }

    public object CinematicsScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        return new LegacySceneFile(0x0003C5B6,
        [
            ComponentActor(
                $"{mapName}_MainSequence",
                S(0, 1.0f, 1.0f, 0),
                S(0, 0, 0, 0, 0),
                $"{mapNameLower}_mainsequence.tpl",
                paths.MapSubFolder(mapNameLower, "cinematics"),
                S(0, 0, 1, 0x677B269Bu, Z(16)))
        ]);
    }

    public object GraphScene() => new LegacySceneFile(
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

    public string GenerateJd2014TimelineActorName(IntermediateSongPackage package) =>
        $"timeline: {package.Metadata.MapName} ({package.Pictograms.Clips.Count} P ; " +
        $"{package.CoachTimelines.Sum(timeline => timeline.Clips.Count)}/" +
        $"{package.FullBodyCoachTimelines.Sum(timeline => timeline.Clips.Count)} M ; {package.Lyrics.Clips.Count} L)";

    public object VideoScreenActor(string mapNameLower, bool embedded = false) =>
        new LegacyVideoScreenActor(mapNameLower, paths, embedded);

    public object VideoOutputActor(bool embedded = false) => new LegacyVideoOutputActor(paths, embedded);

    public object MenuArtSceneActor(string mapName, string mapNameLower, string suffix, uint bounds0, uint bounds1) =>
        new LegacyMenuArtSceneActor(mapName, mapNameLower, suffix, bounds0, bounds1, paths);

    private object GenerateEmbeddedMenuArtScene(string mapName, string mapNameLower, int coachCount)
    {
        List<object> actors = [];
        if (engineVersion != UbiArtEngineVersion.JD2014)
            actors.Add(CoverActor($"{mapName}_cover_generic", mapNameLower, $"{mapNameLower}_cover_generic.tga", CoverPreData(), null, false));

        actors.Add(CoverActor($"{mapName}_cover_albumcoach", mapNameLower, $"{mapNameLower}_cover_albumcoach.tga", CoverPreData(), null, false));
        actors.Add(CoverActor($"{mapName}_cover_albumbkg", mapNameLower, $"{mapNameLower}_cover_albumbkg.tga", CoverPreData(), null, false));

        if (platform == UbiArtPlatform.Revolution && engineVersion != UbiArtEngineVersion.JD2014)
            actors.Add(CoverActor($"{mapName}_map_bkg", mapNameLower, $"{mapNameLower}_map_bkg.tga", MapBackgroundPreData(), MapBackgroundPostData(), false));

        for (int coachIndex = 1; coachIndex <= Math.Max(1, coachCount); coachIndex++)
            actors.Add(CoverActor($"{mapName}_coach_{coachIndex}", mapNameLower, $"{mapNameLower}_coach_{coachIndex}.tga", CoachPreData(), CoachPostData(), true));

        return new LegacySceneFile(0x0004905D, actors);
    }

    private object MusicTrackActor(string mapNameLower) => ComponentActor(
        "MusicTrack",
        S(0, 1.0f, 1.0f, 0),
        S(F(0x3F901F86u), F(0xBED6581Du), 0, 0, 0),
        UsesLegacyConvertedData ? $"{mapNameLower}_musictrack.main_legacy.tpl" : $"{mapNameLower}_musictrack.tpl",
        UsesLegacyConvertedData ? $"cache/legacyconverteddata/{mapNameLower}/audio/" : paths.MapSubFolder(mapNameLower, "audio"),
        S(2, 0, 1, 0x7A7C235Bu, 0x97CA628Bu, 0x358637BDu));

    private object TimelineActor(string name, string tpl, string mapNameLower, bool karaoke, int tailPrefix) => ComponentActor(
        name,
        S(0x358637BDu, 1.0f, 1.0f, 0),
        S(0xBF9430D3u, F(0x3BC9C90Cu), 0, 0, 0),
        tpl,
        paths.MapSubFolder(mapNameLower, "timeline"),
        karaoke ? S(tailPrefix, 0, 1, 0x231F27DEu, Z(16)) : S(tailPrefix, 0, 1, 0x231F27DEu));

    private static object ComponentActor(string name, object preData, object postData, string tpl, string path, object tail) =>
        new LegacyComponentActor(name, preData, postData, tpl, path, tail);

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
            paths);

    private static bool HasSoundSequence(IntermediateSongPackage package) =>
        (package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1) ||
        package.Vibrations.Clips.Count > 0 ||
        package.HideUserInterface.Clips.Count > 0;

    private static LegacyBinarySequence CoverPreData() => S(0, 0.3f, 0.3f, 0);
    private static LegacyBinarySequence MapBackgroundPreData() => S(0, 256.0f, 128.0f, 0);
    private static LegacyBinarySequence CoachPreData() => S(0, F(0x3E949689u), F(0x3E949689u), 0);
    private static LegacyBinarySequence MapBackgroundPostData() => S(0x44B9ED1Fu, 350.0f, 0, 0, 0, uint.MaxValue, 0);
    private static LegacyBinarySequence CoachPostData() => S(0x4354C8D5u, 0x4425EB88u, 0, 0, 0, uint.MaxValue, 0);
}
