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

    public object GenerateSongDesc(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string mapRoot = GetMapRoot(mapNameLower);
        List<object> phoneImages = [new { KEY = "cover", VAL = $"{mapRoot}/menuart/textures/{mapNameLower}_cover_phone.png" }];
        phoneImages.AddRange(Enumerable.Range(1, package.Metadata.CoachCount)
            .Select(index => (object)new { KEY = $"coach{index}", VAL = $"{mapRoot}/menuart/textures/{mapNameLower}_coach_{index}_phone.png" }));

        List<object> defaultColors =
        [
            new { KEY = "lyrics", VAL = ToUbiArtColor(package.Metadata.LyricsColor) },
            new { KEY = "theme", VAL = "0xffffffff" }
        ];
        AddSongColor(defaultColors, package, "songcolor_1a", "songColor_1A");
        AddSongColor(defaultColors, package, "songcolor_1b", "songColor_1B");
        AddSongColor(defaultColors, package, "songcolor_2a", "songColor_2A");
        AddSongColor(defaultColors, package, "songcolor_2b", "songColor_2B");

        object songInfo = new
        {
            MapName = mapName,
            JDVersion = (int)version,
            package.Metadata.OriginalJDVersion,
            RelatedAlbums = Array.Empty<object>(),
            package.Metadata.Artist,
            package.Metadata.Title,
            package.Metadata.Credits,
            PhoneImages = phoneImages,
            NumCoach = package.Metadata.CoachCount,
            MainCoach = -1,
            package.Metadata.Difficulty,
            package.Metadata.SweatDifficulty,
            Tags = package.Metadata.Tags.Select(tag => new { VAL = tag }).ToArray(),
            package.Metadata.Status,
            package.Metadata.MojoValue,
            package.Metadata.CountInProgression,
            GameModes = new[]
            {
                new
                {
                    NAME = "GameModeDesc",
                    GameModeDesc = new
                    {
                        mode = new LuaExpression("GameMode.Classic"),
                        flags = new LuaExpression("GameModeFlags.None"),
                        status = new LuaExpression("GameModeStatus.Available")
                    }
                }
            },
            DefaultColors = defaultColors,
            AudioPreviewFadeTime = 0.5,
            AudioPreviews = new object[]
            {
                new { NAME = "AudioPreview", AudioPreview = new { name = "coverflow", startbeat = package.TimelineStructure.PreviewEntryBeat } },
                new { NAME = "AudioPreview", AudioPreview = new { name = "prelobby", startbeat = package.TimelineStructure.PreviewLoopStartBeat, endbeat = package.TimelineStructure.PreviewLoopEndBeat } }
            }
        };

        object document = new
        {
            NAME = "Actor_Template",
            Actor_Template = new
            {
                TAGS = new[] { new { VAL = "songdescmain" } },
                COMPONENTS = new[] { new { NAME = "JD_SongDescTemplate", JD_SongDescTemplate = songInfo } }
            }
        };
        return LuaDocumentWriter.Write(document, includes: ["EngineData/Helpers/SongDatabase.ilu"]);
    }

    public object GenerateMusicTrack(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        object document = new
        {
            NAME = "Actor_Template",
            Actor_Template = new
            {
                COMPONENTS = new[]
                {
                    new
                    {
                        NAME = "MusicTrackComponent_Template",
                        MusicTrackComponent_Template = new
                        {
                            trackData = new
                            {
                                MusicTrackData = new
                                {
                                    path = $"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}.wav",
                                    structure = new LuaExpression("structure"),
                                    volume = 0
                                }
                            }
                        }
                    }
                }
            }
        };
        return Lua(document, includes: [$"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}.trk"]);
    }

    public object GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> allClips = [];
        List<object> tracks = [];
        HashSet<long> moveTrackIds = [];

        // Add MotionClips for each coach
        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                allClips.Add(new
                {
                    NAME = "MotionClip",
                    MotionClip = new
                    {
                        clip.Id,
                        timeline.TrackId,
                        IsActive = 1,
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"{GetMapRoot(mapNameLower)}/timeline/moves/{clip.MoveId}.msm",
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        timeline.CoachId,
                        MoveType = 0,
                        Color = ParseColorToHex(move.Color)
                    }
                });
            }

            // Add track for this coach
            if (moveTrackIds.Add(timeline.TrackId))
            {
                tracks.Add(new
                {
                    NAME = "MoveTrack",
                    MoveTrack = new
                    {
                        Id = timeline.TrackId,
                        Name = $"Coach{timeline.CoachId}"
                    }
                });
            }
        }

        foreach (MoveTimeline timeline in package.FullBodyCoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.FullBodyCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                allClips.Add(new
                {
                    NAME = "MotionClip",
                    MotionClip = new
                    {
                        clip.Id,
                        timeline.TrackId,
                        IsActive = 1,
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"{GetMapRoot(mapNameLower)}/timeline/moves/{clip.MoveId}.gesture",
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        timeline.CoachId,
                        MoveType = 1,
                        Color = ParseColorToHex(move.Color)
                    }
                });
            }

            if (moveTrackIds.Add(timeline.TrackId))
            {
                tracks.Add(new
                {
                    NAME = "MoveTrack",
                    MoveTrack = new
                    {
                        Id = timeline.TrackId,
                        Name = $"Coach{timeline.CoachId}"
                    }
                });
            }
        }

        // Add PictogramClips
        foreach (PictogramClip pictoClip in package.Pictograms.Clips)
        {
            allClips.Add(new
            {
                NAME = "PictogramClip",
                PictogramClip = new
                {
                    pictoClip.Id,
                    TrackId = PictoTrackId,
                    pictoClip.StartTime,
                    pictoClip.Duration,
                    PictoPath = $"{GetMapRoot(mapNameLower)}/timeline/pictos/{pictoClip.PictogramId}{(version == UbiArtEngineVersion.JD2014 ? ".tga" : ".png")}",
                }
            });
        }

        // Add GoldEffectClips
        foreach (GoldEffectClip goldClip in package.GoldEffects.Clips)
        {
            allClips.Add(new
            {
                NAME = "GoldEffectClip",
                GoldEffectClip = new
                {
                    goldClip.Id,
                    TrackId = GoldEffectTrackId,
                    goldClip.StartTime,
                    goldClip.Duration,
                    goldClip.EffectType
                }
            });
        }

        // Add PictoTrack
        tracks.Add(new
        {
            NAME = "PictoTrack",
            PictoTrack = new
            {
                Id = PictoTrackId,
                Name = "Pictos"
            }
        });

        // Add GoldEffectTrack
        tracks.Add(new
        {
            NAME = "GoldEffectTrack",
            GoldEffectTrack = new
            {
                Id = GoldEffectTrackId,
                Name = "GoldMoveEffects"
            }
        });

        var danceTape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = allClips.OrderBy(c => GetStartTime(c)).ToArray(),
                Tracks = tracks.ToArray(),
                TapeClock = 0,
                package.Metadata.MapName
            }
        };

        return Lua(danceTape);
    }

    public object GenerateKaraokeTape(IntermediateSongPackage package)
    {
        var clips = package.Lyrics.Clips.Select(c => new
        {
            NAME = "KaraokeClip",
            KaraokeClip = new
            {
                c.Id,
                TrackId = 0,
                IsActive = 1,
                c.StartTime,
                c.Duration,
                c.Lyrics,
                Pitch = c.Pitch > 0 ? c.Pitch : 8.175798,
                IsEndOfLine = c.IsEndOfLine ? 1 : 0,
                ContentType = c.ContentType > 0 ? c.ContentType : 2
            }
        }).OrderBy(c => c.KaraokeClip.StartTime).ToArray();

        var karaokeTape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = clips,
                TapeClock = 0,
                package.Metadata.MapName
            }
        };

        return Lua(karaokeTape);
    }

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

    #endregion

    #region Binary/Special Generators (return empty for uncooked as these are typically cooked-only)

    public object GenerateVideoPlayerActor(string mapName, bool isPreview)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string videoFileName = string.IsNullOrWhiteSpace(VideoFileName) ? $"{mapNameLower}.webm" : VideoFileName;
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

    #endregion

    #region Helper Methods

    private string GetMapRoot(string mapNameLower) => version switch
    {
        UbiArtEngineVersion.JD2014 => $"world/maps/jd5/{mapNameLower}",
        UbiArtEngineVersion.JD2015 => $"world/maps/jd2015/{mapNameLower}",
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
