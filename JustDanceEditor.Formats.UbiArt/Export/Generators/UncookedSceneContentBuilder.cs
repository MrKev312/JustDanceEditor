using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal sealed class UncookedSceneContentBuilder(UbiArtEngineVersion version, string? configuredVideoFileName)
{
    private static byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);
    public object GenerateMainScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return Scene([
            SubSceneActor($"{mapName}_AUDIO", $"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}_audio.isc"),
            SubSceneActor($"{mapName}_CINE", $"{GetMapRoot(mapNameLower)}/cinematics/{mapNameLower}_cine.isc"),
            SubSceneActor($"{mapName}_TML", $"{GetMapRoot(mapNameLower)}/timeline/{mapNameLower}_tml.isc"),
            SimpleSceneActor($"{mapName} : SongDesc", $"{GetMapRoot(mapNameLower)}/songdesc.tpl", "JD_SongDescComponent")
        ], sceneConfig: MapSceneConfig());
    }

    public object GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        return Scene(
            SimpleSceneActor(
                "MusicTrack",
                $"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}_musictrack.tpl",
                "MusicTrackComponent"));
    }

    public object GenerateTimelineScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return Scene(
            SimpleSceneActor(
                $"{mapName}_tml_dance",
                $"{GetMapRoot(mapNameLower)}/timeline/{mapNameLower}_tml_dance.tpl",
                "TapeCase_Component",
                $"{mapNameLower}_tml_dance.act"),
            SimpleSceneActor(
                $"{mapName}_tml_karaoke",
                $"{GetMapRoot(mapNameLower)}/timeline/{mapNameLower}_tml_karaoke.tpl",
                "TapeCase_Component",
                $"{mapNameLower}_tml_karaoke.act"));
    }

    public object GenerateCinematicsScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        UbiArtXmlNode masterTape = UbiArtXmlDocumentWriter.Component(
            "MasterTape",
            UbiArtXmlDocumentWriter.Node("MasterTape", new { bankState = 4294967295u }));
        return Scene(UbiArtXmlDocumentWriter.Actor(
            "Actor",
            UbiArtXmlDocumentWriter.StandardActorAttributes(
                $"{mapName}_MainSequence",
                $"{GetMapRoot(mapNameLower)}/cinematics/{mapNameLower}_mainsequence.tpl",
                $"{GetMapRoot(mapNameLower)}/cinematics/{mapNameLower}_mainsequence.act"),
            masterTape));
    }

    public object GenerateMenuArtScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return Scene([
            MaterialSceneActor(
                $"{mapName}_cover_generic",
                $"{GetMapRoot(mapNameLower)}/menuart/textures/{mapNameLower}_cover_generic.tga",
                "0.3 0.3",
                "100.0 100.0"),
            MaterialSceneActor(
                $"{mapName}_coach_1",
                $"{GetMapRoot(mapNameLower)}/menuart/textures/{mapNameLower}_coach_1.tga",
                "0.3 0.3",
                "400.0 100.0"),
            MaterialSceneActor(
                $"{mapName}_map_bkg",
                $"{GetMapRoot(mapNameLower)}/menuart/textures/{mapNameLower}_map_bkg.tga",
                "0.5 0.25",
                "700.0 100.0")
        ], viewFamily: true);
    }

    public object GenerateAutodanceScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        return Scene(SimpleSceneActor(
            $"{mapName}_Autodance",
            $"{GetMapRoot(mapNameLower)}/autodance/{mapNameLower}_autodance.tpl",
            "JD_AutodanceComponent"));
    }

    public object GenerateGraphScene(string mapName)
    {
        return Scene();
    }

    public object GenerateVideoScene(string mapName)
    {
        return Scene(UbiArtXmlDocumentWriter.Actor(
            "Actor",
            UbiArtXmlDocumentWriter.StandardActorAttributes($"{mapName}_video", "", "video_player_main.act")));
    }

    public object GenerateVideoMapPreviewScene(string mapName)
    {
        // Uncooked typically doesn't need map preview scenes, but provide a basic one
        return GenerateVideoScene(mapName);
    }


    public object GenerateVideoPlayerActor(string mapName, bool isPreview)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string videoFileName = string.IsNullOrWhiteSpace(configuredVideoFileName) ? $"{mapNameLower}.webm" : configuredVideoFileName;
        return Lua(Actor(
            "world/_common/videoscreen/video_player_main.tpl",
            new { NAME = "PleoComponent", PleoComponent = new { Video = $"{GetMapRoot(mapNameLower)}/videoscoach/{videoFileName}" } }));
    }

    public object GenerateMpd()
    {
        // MPD files are for cooked platforms - return empty for uncooked
        return Array.Empty<byte>();
    }

    public object GenerateAutodanceActor(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        return Lua(Actor(
            $"{GetMapRoot(mapNameLower)}/autodance/{mapNameLower}_autodance.tpl",
            new Dictionary<string, object?> { ["NAME"] = "JD_AutodanceComponent" }));
    }

    public object GenerateMenuArtActor(string textureName, string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        object component = new
        {
            NAME = "MaterialGraphicComponent",
            MaterialGraphicComponent = new
            {
                material = new
                {
                    GFXMaterialSerializable = new
                    {
                        textureSet = new
                        {
                            GFXMaterialTexturePathSet = new
                            {
                                diffuse = $"{GetMapRoot(mapNameLower)}/menuart/textures/{textureName}.tga"
                            }
                        }
                    }
                }
            }
        };
        return LuaDocumentWriter.Write(Actor("enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl", component));
    }


    private string GetMapRoot(string mapNameLower) => version switch
    {
        UbiArtEngineVersion.JD2014 => $"world/jd5/{mapNameLower}",
        UbiArtEngineVersion.JD2015 => $"world/jd2015/{mapNameLower}",
        _ => $"world/maps/{mapNameLower}"
    };

    private static byte[] Lua(object value, string assignment = "params", IEnumerable<string>? includes = null) =>
        ToBytes(LuaDocumentWriter.Write(value, assignment, includes));

    private static object ActorTemplate(string componentName, object componentValue)
    {
        Dictionary<string, object?> component = new(StringComparer.Ordinal)
        {
            ["NAME"] = componentName,
            [componentName] = componentValue
        };
        return new
        {
            NAME = "Actor_Template",
            Actor_Template = new { COMPONENTS = new object[] { component } }
        };
    }

    private static object Actor(string luaPath, params object[] components) => new
    {
        NAME = "Actor",
        Actor = new { LUA = luaPath, COMPONENTS = components }
    };

    private static byte[] Scene(params UbiArtXmlNode[] actors) => Scene((IEnumerable<UbiArtXmlNode>)actors);

    private static byte[] Scene(
        IEnumerable<UbiArtXmlNode> actors,
        bool viewFamily = false,
        UbiArtXmlNode? sceneConfig = null)
    {
        List<UbiArtXmlNode> children = [.. actors];
        children.Add(sceneConfig ?? UbiArtXmlDocumentWriter.DefaultSceneConfig());
        return UbiArtXmlDocumentWriter.Write(UbiArtXmlDocumentWriter.Scene(children, viewFamily));
    }

    private static UbiArtXmlNode SimpleSceneActor(
        string userFriendly,
        string luaPath,
        string componentName,
        string instanceDataFile = "") =>
        UbiArtXmlDocumentWriter.Actor(
            "Actor",
            UbiArtXmlDocumentWriter.StandardActorAttributes(userFriendly, luaPath, instanceDataFile),
            UbiArtXmlDocumentWriter.Component(componentName));

    private static UbiArtXmlNode SubSceneActor(string userFriendly, string relativePath) =>
        UbiArtXmlDocumentWriter.Actor(
            "SubSceneActor",
            new
            {
                RELATIVEZ = "0.000000",
                SCALE = "1.000000 1.000000",
                xFLIPPED = 0,
                USERFRIENDLY = userFriendly,
                MARKER = "",
                DEFAULTENABLE = 1,
                POS2D = "0.000000 0.000000",
                ANGLE = "0.000000",
                INSTANCEDATAFILE = "enginedata/actortemplates/subscene.tpl",
                LUA = "enginedata/actortemplates/subscene.tpl",
                RELATIVEPATH = relativePath,
                EMBED_SCENE = 0,
                IS_SINGLE_PIECE = 0,
                ZFORCED = 1,
                DIRECT_PICKING = 1
            },
            UbiArtXmlDocumentWriter.Node("ENUM", new { NAME = "viewType", SEL = 2 }));

    private static UbiArtXmlNode MaterialSceneActor(
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
            UbiArtXmlDocumentWriter.StandardActorAttributes(
                userFriendly,
                "enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl",
                scale: scale,
                position: position),
            UbiArtXmlDocumentWriter.Component("MaterialGraphicComponent", material));
    }

    private static UbiArtXmlNode MapSceneConfig()
    {
        UbiArtXmlNode config = UbiArtXmlDocumentWriter.Node(
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
                UbiArtXmlDocumentWriter.Node("sceneConfigs", new { NAME = "JD_MapSceneConfig" }, config)));
    }

}
