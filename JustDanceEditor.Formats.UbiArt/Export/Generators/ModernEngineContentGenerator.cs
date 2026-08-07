using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

public class ModernEngineContentGenerator(UbiArtEngineVersion EngineVersion) : IEngineContentGenerator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);

    #region JSON/Lua Generators

    private readonly ModernGameplayContentBuilder _gameplayBuilder = new(EngineVersion);

    public object GenerateSongDesc(IntermediateSongPackage package) => _gameplayBuilder.GenerateSongDesc(package);
    public object GenerateMusicTrack(IntermediateSongPackage package) => _gameplayBuilder.GenerateMusicTrack(package);
    public object GenerateDanceTape(IntermediateSongPackage package) => _gameplayBuilder.GenerateDanceTape(package);
    public object GenerateKaraokeTape(IntermediateSongPackage package) => _gameplayBuilder.GenerateKaraokeTape(package);

    public object GenerateTapeCaseTpl(string mapName, string tapeType)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string label = tapeType == "dance" ? "tml_motion" : "tml_karaoke";
        string extension = tapeType == "dance" ? "dtape" : "ktape";

        var tpl = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[]
            {
                new
                {
                    __class = "TapeCase_Template",
                    TapesRack = new object[]
                    {
                        new
                        {
                            __class = "TapeGroup",
                            Entries = new object[]
                            {
                                new
                                {
                                    __class = "TapeEntry",
                                    Label = label,
                                    Path = $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_{tapeType}.{extension}"
                                }
                            }
                        }
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(tpl, _jsonOptions));
    }

    public object GenerateSequenceTpl()
    {
        var tpl = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[] { new { __class = "TapeCase_Template" } }
        };
        return ToBytes(JsonSerializer.Serialize(tpl, _jsonOptions));
    }

    public object GenerateSoundTape(string mapName)
    {
        var stape = new
        {
            __class = "Tape",
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            MapName = mapName,
            SoundwichEvent = ""
        };
        return ToBytes(JsonSerializer.Serialize(stape, _jsonOptions));
    }

    public object GenerateAmbTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        var tpl = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[]
            {
                new
                {
                    __class = "SoundComponent_Template",
                    soundList = new object[]
                    {
                        new
                        {
                            __class = "SoundDescriptor_Template",
                            name = $"amb_{mapNameLower}_intro",
                            volume = 6,
                            category = "amb",
                            limitCategory = "",
                            limitMode = 0,
                            maxInstances = 4294967295,
                            @params = new
                            {
                                __class = "SoundParams",
                                delay = 0,
                                fadeInTime = 0,
                                fadeOutTime = 0,
                                filterFrequency = 0,
                                filterType = 2,
                                loop = 0,
                                pitch = 1,
                                playMode = 1,
                                playModeInput = "",
                                randomDelay = 0,
                                randomVolMin = 0,
                                randomVolMax = 0,
                                randomPitchMin = 1,
                                randomPitchMax = 1,
                                transitionSampleOffset = 0
                            },
                            pauseInsensitiveFlags = 0,
                            serialPlayingMode = 0,
                            serialStoppingMode = 0,
                            outDevices = 4294967295,
                            soundPlayAfterdestroy = 0,
                            files = new object[] { $"world/maps/{mapNameLower}/audio/amb/amb_{mapNameLower}_intro.wav" }
                        }
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(tpl, _jsonOptions));
    }

    public object GenerateMainSequenceTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        var tpl = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[]
            {
                new
                {
                    __class = "MasterTape_Template",
                    TapesRack = new object[]
                    {
                        new
                        {
                            __class = "TapeGroup",
                            Entries = new object[]
                            {
                                new
                                {
                                    __class = "TapeEntry",
                                    Label = "master",
                                    Path = $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tape"
                                }
                            }
                        }
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(tpl, _jsonOptions));
    }

    public object GenerateSgs()
    {
        var sgs = new
        {
            settings = new
            {
                __class = "JD_MapSceneConfig",
                Pause_Level = 6,
                name = "",
                type = 1,
                musicscore = 2,
                soundContext = "",
                hud = 0
            }
        };
        return ToBytes(JsonSerializer.Serialize(sgs, _jsonOptions));
    }

    public object GenerateGenericActor(string className, string luaPath)
    {
        var actor = new
        {
            __class = "Actor",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            USERFRIENDLY = "",
            LUA = luaPath,
            COMPONENTS = Array.Empty<object>()
        };
        return ToBytes(JsonSerializer.Serialize(actor, _jsonOptions));
    }

    public object GenerateAutodanceTape(IntermediateSongPackage package)
    {
        var tpl = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[]
            {
                new
                {
                    __class = "JD_AutodanceComponent_Template",
                    song = package.Metadata.MapName,
                    autodanceData = new
                    {
                        __class = "JD_AutodanceData",
                        recording_structure = new
                        {
                            __class = "JD_AutodanceRecordingStructure",
                            records = Array.Empty<object>()
                        },
                        playback_events = Array.Empty<object>()
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(tpl, _jsonOptions));
    }

    public object GenerateMainSequenceTape(IntermediateSongPackage package)
    {
        List<object> clips = [];
        long clipIdCounter = 12345;

        if (package.Vibrations?.Clips != null)
        {
            foreach (VibrationClip vibrationClip in package.Vibrations.Clips)
            {
                clips.Add(new
                {
                    __class = "VibrationClip",
                    Id = vibrationClip.Id != 0 ? vibrationClip.Id : clipIdCounter++,
                    TrackId = vibrationClip.TrackId != 0 ? vibrationClip.TrackId : 3606330319L,
                    IsActive = 1,
                    vibrationClip.StartTime,
                    vibrationClip.Duration,
                    VibrationFilePath = string.IsNullOrWhiteSpace(vibrationClip.VibrationFilePath)
                        ? "world/_common/hd_rumble/bigpulse_01.vib"
                        : vibrationClip.VibrationFilePath,
                    vibrationClip.Loop,
                    vibrationClip.DeviceSide,
                    PlayerId = vibrationClip.PlayerId ?? -1,
                    vibrationClip.Context,
                    vibrationClip.StartTimeOffset,
                    Modulation = vibrationClip.Modulation ?? 0.5f
                });
            }
        }

        if (package.HideUserInterface?.Clips != null)
        {
            long trackId = 1111;
            foreach (HideUserInterfaceClip hideClip in package.HideUserInterface.Clips)
            {
                clips.Add(new
                {
                    __class = "HideUserInterfaceClip",
                    Id = hideClip.Id != 0 ? hideClip.Id : clipIdCounter++,
                    TrackId = trackId,
                    IsActive = hideClip.IsActive ? 1 : 0,
                    hideClip.StartTime,
                    hideClip.Duration,
                    EventType = 1,
                    CustomParam = ""
                });
                trackId++;
            }
        }

        if (package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1)
        {
            long ambClipId = 67890;
            long ambTrackId = 2222;
            int ambDuration = 1200;
            clips.Add(new
            {
                __class = "SoundSetClip",
                Id = ambClipId,
                TrackId = ambTrackId,
                IsActive = 1,
                StartTime = package.TimelineStructure.StartBeat * 24,
                Duration = ambDuration,
                SoundSetPath = $"world/maps/{package.Metadata.MapName.ToLowerInvariant()}/audio/amb/amb_{package.Metadata.MapName.ToLowerInvariant()}_intro.tpl",
                SoundChannel = 0,
                StartOffset = 0,
                StopsOnEnd = 0,
                AccountedForDuration = 0
            });
        }

        object tape = new
        {
            __class = "Tape",
            Clips = clips.OrderBy(c => ((dynamic)c).StartTime).ToList(),
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            package.Metadata.MapName,
            SoundwichEvent = ""
        };
        return ToBytes(JsonSerializer.Serialize(tape, _jsonOptions));
    }

    #endregion

    #region XML Scene Generators

    private static readonly ModernSceneContentBuilder SceneBuilder = new();

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
    public virtual object GenerateMenuArtActor(string textureName, string mapName) => SceneBuilder.GenerateMenuArtActor(textureName, mapName);

    #endregion
}
