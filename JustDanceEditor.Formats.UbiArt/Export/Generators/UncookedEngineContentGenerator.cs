using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Reflection;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

/// <summary>
/// Engine content generator that produces Lua-formatted output for Uncooked UbiArt packages.
/// Uncooked packages are raw, human-readable project files used during development and modding.
/// </summary>
public class UncookedEngineContentGenerator(UbiArtEngineVersion version = UbiArtEngineVersion.JD2022) : IEngineContentGenerator
{
    private const long PictoTrackId = 1272115770L;
    private const long GoldEffectTrackId = 628418524L;

    private static byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);

    public string? VideoFileName { get; set; }

    #region Lua Content Generators

    private readonly UncookedGameplayContentBuilder _gameplayBuilder = new(version);

    public object GenerateSongDesc(IntermediateSongPackage package) => _gameplayBuilder.GenerateSongDesc(package);
    public object GenerateMusicTrack(IntermediateSongPackage package) => _gameplayBuilder.GenerateMusicTrack(package);
    public object GenerateDanceTape(IntermediateSongPackage package) => _gameplayBuilder.GenerateDanceTape(package);
    public object GenerateKaraokeTape(IntermediateSongPackage package) => _gameplayBuilder.GenerateKaraokeTape(package);

    public object GenerateTapeCaseTpl(string mapName, string tapeType)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string extension = tapeType == "dance" ? "dtape" : "ktape";

        object entry = new
        {
            TapeEntry = new
            {
                Label = tapeType,
                Path = $"{GetMapRoot(mapNameLower)}/timeline/{mapNameLower}_tml_{tapeType}.{extension}"
            }
        };
        return Lua(ActorTemplate("TapeCase_Template", new
        {
            TapesRack = new[] { new { TapeGroup = new { Entries = new[] { entry } } } }
        }));
    }

    public object GenerateSequenceTpl()
    {
        return Lua(ActorTemplate("TapeCase_Template", new Dictionary<string, object?>()));
    }

    public object GenerateSoundTape(string mapName)
    {
        var stape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = Array.Empty<object>(),
                TapeClock = 0,
                MapName = mapName
            }
        };

        return Lua(stape);
    }

    public object GenerateAmbTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        object descriptor = new
        {
            SoundDescriptor_Template = new
            {
                name = $"amb_{mapNameLower}_intro",
                volume = 6,
                category = "amb",
                limitMode = 0,
                @params = new { SoundParams = new { loop = 0, playMode = 1 } },
                files = new[] { $"{GetMapRoot(mapNameLower)}/audio/amb/amb_{mapNameLower}_intro.wav" }
            }
        };
        return Lua(ActorTemplate("SoundComponent_Template", new { soundList = new[] { descriptor } }));
    }

    public object GenerateMainSequenceTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        object entry = new
        {
            TapeEntry = new
            {
                Label = "Master",
                Path = $"{GetMapRoot(mapNameLower)}/cinematics/{mapNameLower}_mainsequence.tape"
            }
        };
        return Lua(ActorTemplate("MasterTape_Template", new
        {
            TapesRack = new[] { new { TapeGroup = new { Entries = new[] { entry } } } }
        }));
    }

    public object GenerateSgs()
    {
        return Lua(new
        {
            JD_MapSceneConfig = new { Pause_Level = 6, name = "", type = 1, musicscore = 2, soundContext = "", hud = 0 }
        }, assignment: "settings");
    }

    public object GenerateGenericActor(string className, string luaPath)
    {
        return Lua(Actor(luaPath, new Dictionary<string, object?> { ["NAME"] = className }));
    }

    public object GenerateAutodanceTape(IntermediateSongPackage package)
    {
        object data = new
        {
            song = package.Metadata.MapName,
            autodanceData = new
            {
                JD_AutodanceData = new
                {
                    recording_structure = new { JD_AutodanceRecordingStructure = new { records = Array.Empty<object>() } },
                    playback_events = Array.Empty<object>()
                }
            }
        };
        return Lua(ActorTemplate("JD_AutodanceComponent_Template", data));
    }

    public object GenerateMainSequenceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> clips = [];
        List<object> tracks = [];

        // Add SoundSetClip for AMB intro if there's a negative start beat
        if (package.TimelineStructure.StartBeat < 0)
        {
            long ambTrackId = 2222;
            long ambClipId = 67890;
            int startTime = package.TimelineStructure.StartBeat * 24;
            int duration = Math.Abs(package.TimelineStructure.StartBeat) * 24;

            clips.Add(new
            {
                NAME = "SoundSetClip",
                SoundSetClip = new
                {
                    Id = ambClipId,
                    TrackId = ambTrackId,
                    IsActive = 1,
                    StartTime = startTime,
                    Duration = duration,
                    SoundSetPath = $"{GetMapRoot(mapNameLower)}/audio/amb/amb_{mapNameLower}_intro.tpl"
                }
            });

            tracks.Add(new
            {
                NAME = "TapeTrack",
                TapeTrack = new
                {
                    Id = ambTrackId,
                    Name = "SOUND"
                }
            });
        }

        var tape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = clips.ToArray(),
                Tracks = tracks.ToArray(),
                TapeClock = 0
            }
        };

        return Lua(tape);
    }

    #endregion

    #region XML Scene Generators (XML is the same for Uncooked, not JSON)

    private UncookedSceneContentBuilder SceneBuilder => new(version, VideoFileName);

    public object GenerateMainScene(IntermediateSongPackage package) => SceneBuilder.GenerateMainScene(package);
    public object GenerateAudioScene(IntermediateSongPackage package) => SceneBuilder.GenerateAudioScene(package);
    public object GenerateTimelineScene(IntermediateSongPackage package) => SceneBuilder.GenerateTimelineScene(package);
    public object GenerateCinematicsScene(IntermediateSongPackage package) => SceneBuilder.GenerateCinematicsScene(package);
    public object GenerateMenuArtScene(IntermediateSongPackage package) => SceneBuilder.GenerateMenuArtScene(package);
    public object GenerateAutodanceScene(IntermediateSongPackage package) => SceneBuilder.GenerateAutodanceScene(package);
    public object GenerateGraphScene(string mapName) => SceneBuilder.GenerateGraphScene(mapName);
    public object GenerateVideoScene(string mapName) => SceneBuilder.GenerateVideoScene(mapName);
    public object GenerateVideoMapPreviewScene(string mapName) => SceneBuilder.GenerateVideoMapPreviewScene(mapName);
    public object GenerateVideoPlayerActor(string mapName, bool isPreview) => SceneBuilder.GenerateVideoPlayerActor(mapName, isPreview);
    public object GenerateMpd() => SceneBuilder.GenerateMpd();
    public object GenerateAutodanceActor(string mapName) => SceneBuilder.GenerateAutodanceActor(mapName);
    public object GenerateMenuArtActor(string textureName, string mapName) => SceneBuilder.GenerateMenuArtActor(textureName, mapName);

    private string GetMapRoot(string mapNameLower) => version switch
    {
        UbiArtEngineVersion.JD2014 => $"world/jd5/{mapNameLower}",
        UbiArtEngineVersion.JD2015 => $"world/jd2015/{mapNameLower}",
        _ => $"world/maps/{mapNameLower}"
    };

    private static string ParseColorToHex(string hexColor)
    {
        return ToUbiArtColor(hexColor);
    }

    private static void AddSongColor(List<object> colors, IntermediateSongPackage package, string metadataKey, string ubiArtKey)
    {
        if (package.Metadata.AdditionalMetadata.TryGetValue(metadataKey, out string? color) && !string.IsNullOrWhiteSpace(color))
            colors.Add(new { KEY = ubiArtKey, VAL = ToUbiArtColor(color) });
    }

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

    private static string ToUbiArtColor(string color)
    {
        string hex = color.Trim().TrimStart('#');
        return hex.Length switch
        {
            8 => $"0x{hex[6..8]}{hex[..6]}",
            6 => $"0xFF{hex}",
            _ => "0xFFFF8080"
        };
    }

    private static int GetStartTime(object clip)
    {
        // Use reflection to get StartTime from the anonymous type
        Type type = clip.GetType();
        PropertyInfo? nameProperty = type.GetProperty("NAME");
        if (nameProperty?.GetValue(clip) is string name)
        {
            PropertyInfo? innerProperty = type.GetProperty(name);
            if (innerProperty?.GetValue(clip) is object inner)
            {
                PropertyInfo? startTimeProperty = inner.GetType().GetProperty("StartTime");
                if (startTimeProperty?.GetValue(inner) is int startTime)
                    return startTime;
            }
        }

        return 0;
    }

    #endregion
}
