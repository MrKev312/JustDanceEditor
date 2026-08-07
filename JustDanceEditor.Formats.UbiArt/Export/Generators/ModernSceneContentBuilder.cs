using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal sealed class ModernSceneContentBuilder
{
    public object GenerateMainScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        List<UbiArtXmlNode> children =
        [
            PlatformFilter("WII", $"{mapName}_AUTODANCE"),
            EmbeddedSubScene($"{mapName}_AUDIO", $"world/maps/{mapNameLower}/audio/{mapNameLower}_audio.isc", GenerateAudioScene(package)),
            EmbeddedSubScene($"{mapName}_CINE", $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_cine.isc", GenerateCinematicsScene(package)),
            EmbeddedSubScene($"{mapName}_GRAPH", $"world/maps/{mapNameLower}/graph/{mapNameLower}_graph.isc", GenerateGraphScene(mapName)),
            EmbeddedSubScene($"{mapName}_TML", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml.isc", GenerateTimelineScene(package)),
            EmbeddedSubScene($"{mapName}_VIDEO", $"world/maps/{mapNameLower}/videoscoach/{mapNameLower}_video.isc", GenerateVideoScene(mapName)),
            ModernActor(
                $"{mapName} : SongDesc",
                $"world/maps/{mapNameLower}/songdesc.tpl",
                "JD_SongDescComponent",
                position: "-3.531976 -1.485322"),
            EmbeddedSubScene(
                $"{mapName}_menuart",
                $"world/maps/{mapNameLower}/menuart/{mapNameLower}_menuart.isc",
                GenerateMenuArtScene(package),
                viewType: 3),
            EmbeddedSubScene(
                $"{mapName}_AUTODANCE",
                $"world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.isc",
                GenerateAutodanceScene(package),
                position: "0.000000 -0.033823"),
            ModernMapSceneConfig()
        ];
        UbiArtSceneSettings settings = new("326704", "2.000000", ViewFamily: 0, IsPopup: 0);
        return UbiArtXmlDocumentWriter.Write(UbiArtXmlDocumentWriter.Scene(children, settings));
    }
    public object GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        return ModernScene(
            ModernActor("MusicTrack", $"world/maps/{mapNameLower}/audio/{mapNameLower}_musictrack.tpl", "MusicTrackComponent", position: "1.125962 -0.418641"),
            ModernActor($"{package.Metadata.MapName}_sequence", $"world/maps/{mapNameLower}/audio/{mapNameLower}_sequence.tpl", "TapeCase_Component", relativeZ: "0.000001", position: "-0.006158 -0.006158"));
    }

    public object GenerateTimelineScene(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        return ModernScene(
            ModernActor($"{package.Metadata.MapName}_tml_dance", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl", "TapeCase_Component", relativeZ: "0.000001", position: "-1.157740 0.006158"),
            ModernActor($"{package.Metadata.MapName}_tml_karaoke", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl", "TapeCase_Component", relativeZ: "0.000001", position: "-1.157740 0.006158"));
    }

    public object GenerateCinematicsScene(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        return ModernScene(ModernActor(
            $"{package.Metadata.MapName}_MainSequence",
            $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl",
            "MasterTape"));
    }

    public object GenerateMenuArtScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string textureRoot = $"world/maps/{mapNameLower}/menuart/textures";
        List<UbiArtXmlNode> children =
        [
            PlatformFilter("WIIU", $"{mapName}_cover_generic", $"{mapName}_cover_albumbkg"),
            PlatformFilter("ORBIS", $"{mapName}_cover_generic", $"{mapName}_cover_albumbkg"),
            ModernMaterialActor($"{mapName}_cover_generic", $"{textureRoot}/{mapNameLower}_cover_generic.tga", "0.300000 0.300000", "266.087555 197.629959"),
            ModernMaterialActor($"{mapName}_cover_online", $"{textureRoot}/{mapNameLower}_cover_online.tga", "0.300000 0.300000", "-150.000000 0.000000"),
            ModernMaterialActor($"{mapName}_cover_albumcoach", $"{textureRoot}/{mapNameLower}_cover_albumcoach.tga", "0.300000 0.300000", "738.106323 359.612030"),
            ModernMaterialActor($"{mapName}_cover_albumbkg", $"{textureRoot}/{mapNameLower}_cover_albumbkg.tga", "0.300000 0.300000", "1067.972168 201.986328"),
            ModernMaterialActor($"{mapName}_banner_bkg", $"{textureRoot}/{mapNameLower}_banner_bkg.tga", "256.000000 128.000000", "1487.410156 -32.732918"),
            ModernMaterialActor($"{mapName}_coach_1", $"{textureRoot}/{mapNameLower}_coach_1.tga", "0.290211 0.290211", "212.784500 663.680176"),
            ModernMaterialActor($"{mapName}_map_bkg", $"{textureRoot}/{mapNameLower}_map_bkg.tga", "256.000000 128.000000", "1487.410034 350.000000"),
            UbiArtXmlDocumentWriter.DefaultSceneConfig()
        ];
        UbiArtSceneSettings settings = new("326704", "0.500000", ViewFamily: 1, IsPopup: 0);
        return UbiArtXmlDocumentWriter.Write(UbiArtXmlDocumentWriter.Scene(children, settings));
    }
    public object GenerateAutodanceScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        return ModernScene(ModernActor(
            $"{mapName}_Autodance",
            $"world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.tpl",
            "JD_AutodanceComponent"));
    }

    public object GenerateGraphScene(string mapName)
    {
        return ModernScene(UbiArtXmlDocumentWriter.Actor(
            "Actor",
            ModernActorAttributes(
                "Camera_JD_Dummy",
                "enginedata/actortemplates/tpl_emptyactor.tpl",
                instanceDataFile: "enginedata/actortemplates/tpl_emptyactor.tpl",
                relativeZ: "10.000000")));
    }

    public object GenerateVideoScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        UbiArtXmlNode pleo = UbiArtXmlDocumentWriter.Component(
            "PleoComponent",
            UbiArtXmlDocumentWriter.Node("PleoComponent", new
            {
                video = $"world/maps/{mapNameLower}/videoscoach/{mapNameLower}.webm",
                dashMPD = $"world/maps/{mapNameLower}/videoscoach/{mapNameLower}.mpd"
            }));
        UbiArtXmlNode material = UbiArtXmlDocumentWriter.Node(
            "material",
            null,
            UbiArtXmlDocumentWriter.Node(
                "GFXMaterialSerializable",
                null,
                UbiArtXmlDocumentWriter.Node(
                    "textureSet",
                    null,
                    UbiArtXmlDocumentWriter.Node("GFXMaterialTexturePathSet"))));
        UbiArtXmlNode output = UbiArtXmlDocumentWriter.Component("PleoTextureGraphicComponent", material);
        return ModernScene(
            UbiArtXmlDocumentWriter.Actor(
                "Actor",
                ModernActorAttributes("VideoScreen", "world/_common/videoscreen/video_player_main.tpl", relativeZ: "-1.000000", position: "0.000000 -4.500000"),
                pleo),
            UbiArtXmlDocumentWriter.Actor(
                "Actor",
                ModernActorAttributes("VideoOutput", "world/_common/videoscreen/video_output_main.tpl", scale: "3.941238 2.220000"),
                output));
    }

    public object GenerateVideoMapPreviewScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        UbiArtXmlNode pleo = UbiArtXmlDocumentWriter.Component(
            "PleoComponent",
            UbiArtXmlDocumentWriter.Node("PleoComponent", new
            {
                video = $"world/maps/{mapNameLower}/videoscoach/{mapNameLower}.webm",
                dashMPD = $"world/maps/{mapNameLower}/videoscoach/{mapNameLower}.mpd",
                channelID = mapName
            }));
        return ModernScene(UbiArtXmlDocumentWriter.Actor(
            "Actor",
            ModernActorAttributes("VideoScreen", "world/_common/videoscreen/video_player_map_preview.tpl", relativeZ: "-1.000000", position: "0.000000 -4.500000"),
            pleo));
    }



    public object GenerateVideoPlayerActor(string mapName, bool isPreview)
    {
        return new UbiArtVideoPlayerActorFile(mapName, isPreview);
    }

    public object GenerateMpd()
    {
        return new UbiArtMpdFile();
    }

    public object GenerateAutodanceActor(string mapName)
    {
        return new UbiArtAutodanceActorFile(mapName);
    }

    public object GenerateMenuArtActor(string textureName, string mapName)
    {
        return new UbiArtMenuArtActorFile(textureName, mapName, UbiArtMenuArtActorVersion.Modern);
    }

    private static byte[] ModernScene(params UbiArtXmlNode[] actors)
    {
        List<UbiArtXmlNode> children = [.. actors, UbiArtXmlDocumentWriter.DefaultSceneConfig()];
        UbiArtSceneSettings settings = new("326704", "0.500000", ViewFamily: 0, IsPopup: 0);
        return UbiArtXmlDocumentWriter.Write(UbiArtXmlDocumentWriter.Scene(children, settings));
    }

    private static UbiArtXmlNode ModernActor(
        string userFriendly,
        string lua,
        string componentName,
        string relativeZ = "0.000000",
        string position = "0.000000 0.000000") =>
        UbiArtXmlDocumentWriter.Actor(
            "Actor",
            ModernActorAttributes(userFriendly, lua, relativeZ: relativeZ, position: position),
            UbiArtXmlDocumentWriter.Component(componentName));

    private static object ModernActorAttributes(
        string userFriendly,
        string lua,
        string instanceDataFile = "",
        string relativeZ = "0.000000",
        string scale = "1.000000 1.000000",
        string position = "0.000000 0.000000") => new
        {
            RELATIVEZ = relativeZ,
            SCALE = scale,
            xFLIPPED = 0,
            USERFRIENDLY = userFriendly,
            MARKER = "",
            DEFAULTENABLE = 1,
            POS2D = position,
            ANGLE = "0.000000",
            INSTANCEDATAFILE = instanceDataFile,
            LUA = lua
        };

    private static UbiArtXmlNode PlatformFilter(string platform, params string[] objects) =>
        UbiArtXmlDocumentWriter.Node(
            "PLATFORM_FILTER",
            null,
            UbiArtXmlDocumentWriter.Node(
                "TargetFilterList",
                new { platform },
                [.. objects.Select(value => UbiArtXmlDocumentWriter.Node("objects", new { VAL = value }))]));

    private static UbiArtXmlNode EmbeddedSubScene(
        string userFriendly,
        string relativePath,
        object sceneContent,
        int viewType = 2,
        string position = "0.000000 0.000000")
    {
        UbiArtXmlNode root = UbiArtXmlDocumentWriter.Read(UbiArtEngineContentSerializer.Serialize(sceneContent));
        UbiArtXmlNode scene = root.Children.Single(child => child.Name == "Scene");
        return UbiArtXmlDocumentWriter.Actor(
            "SubSceneActor",
            new
            {
                RELATIVEZ = "0.000000",
                SCALE = "1.000000 1.000000",
                xFLIPPED = 0,
                USERFRIENDLY = userFriendly,
                MARKER = "",
                DEFAULTENABLE = 1,
                POS2D = position,
                ANGLE = "0.000000",
                INSTANCEDATAFILE = "enginedata/actortemplates/subscene.tpl",
                LUA = "enginedata/actortemplates/subscene.tpl",
                RELATIVEPATH = relativePath,
                EMBED_SCENE = 1,
                IS_SINGLE_PIECE = 0,
                ZFORCED = 1,
                DIRECT_PICKING = 1,
                IGNORE_SAVE = 0
            },
            UbiArtXmlDocumentWriter.Node("ENUM", new { NAME = "viewType", SEL = viewType }),
            UbiArtXmlDocumentWriter.Node("SCENE", null, scene));
    }

    private static UbiArtXmlNode ModernMaterialActor(
        string userFriendly,
        string texturePath,
        string scale,
        string position)
    {
        UbiArtXmlNode material = UbiArtXmlDocumentWriter.Node(
            "material",
            null,
            UbiArtXmlDocumentWriter.Node(
                "GFXMaterialSerializable",
                null,
                UbiArtXmlDocumentWriter.Node(
                    "textureSet",
                    null,
                    UbiArtXmlDocumentWriter.Node("GFXMaterialTexturePathSet", new { diffuse = texturePath }))));
        return UbiArtXmlDocumentWriter.Actor(
            "Actor",
            ModernActorAttributes(
                userFriendly,
                "enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl",
                scale: scale,
                position: position),
            UbiArtXmlDocumentWriter.Component("MaterialGraphicComponent", material));
    }

    private static UbiArtXmlNode ModernMapSceneConfig()
    {
        UbiArtXmlNode mapConfig = UbiArtXmlDocumentWriter.Node(
            "JD_MapSceneConfig",
            new { name = "", soundContext = "", hud = 0 },
            UbiArtXmlDocumentWriter.Node("ENUM", new { NAME = "Pause_Level", SEL = 6 }),
            UbiArtXmlDocumentWriter.Node("ENUM", new { NAME = "type", SEL = 1 }),
            UbiArtXmlDocumentWriter.Node("ENUM", new { NAME = "musicscore", SEL = 2 }));
        return UbiArtXmlDocumentWriter.Node(
            "sceneConfigs",
            null,
            UbiArtXmlDocumentWriter.Node(
                "SceneConfigs",
                new { activeSceneConfig = 0 },
                UbiArtXmlDocumentWriter.Node("sceneConfigs", new { NAME = "JD_MapSceneConfig" }, mapConfig)));
    }

}

