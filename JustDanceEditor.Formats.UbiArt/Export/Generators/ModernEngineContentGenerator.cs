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

    public object GenerateSongDesc(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        var songDesc = new
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
                    __class = "JD_SongDescTemplate",
                    package.Metadata.MapName,
                    JDVersion = (int)EngineVersion,
                    package.Metadata.OriginalJDVersion,
                    package.Metadata.Artist,
                    DancerName = "Unknown Dancer",
                    package.Metadata.Title,
                    Credits = package.Metadata.Credits ?? "",
                    PhoneImages = new
                    {
                        cover = $"world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_phone.jpg",
                        coach1 = $"world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_coach_1_phone.png"
                    },
                    NumCoach = package.Metadata.CoachCount,
                    MainCoach = -1,
                    package.Metadata.Difficulty,
                    package.Metadata.SweatDifficulty,
                    backgroundType = 0,
                    LyricsType = 0,
                    Tags = new[] { "main" },
                    Status = 3,
                    LocaleID = 4294967295,
                    MojoValue = 0,
                    CountInProgression = 1,
                    DefaultColors = new
                    {
                        songcolor_1a = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1a", "#FFFFFFFF")),
                        songcolor_1b = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1b", "#FFFFFFFF")),
                        songcolor_2a = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2a", "#FFFFFFFF")),
                        songcolor_2b = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2b", "#FFFFFFFF")),
                        lyrics = ConvertColorToArray(package.Metadata.LyricsColor),
                        theme = new[] { 1, 1, 1, 1 }
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(songDesc, _jsonOptions));
    }

    public object GenerateMusicTrack(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        Signature[] signatures = [.. package.TimelineStructure.Signatures.Select(s => new Signature { Beats = s.Beats, Marker = s.Marker })];
        Section[] sections = [.. package.TimelineStructure.Sections.Select(sec => new Section { Marker = (float)sec.StartBeat, SectionType = (int)sec.SectionType, Comment = sec.Comment ?? string.Empty })];

        Structure structure = new()
        {
            StartBeat = package.TimelineStructure.StartBeat,
            EndBeat = package.TimelineStructure.EndBeat,
            VideoStartTime = (float)package.TimelineStructure.VideoStartOffset,
            PreviewEntry = package.TimelineStructure.PreviewEntryBeat,
            PreviewLoopStart = package.TimelineStructure.PreviewLoopStartBeat,
            PreviewLoopEnd = package.TimelineStructure.PreviewLoopEndBeat,
            PreviewDuration = package.TimelineStructure.PreviewDuration,
            Markers = [.. package.TimelineStructure.Markers],
            Signatures = signatures,
            Sections = sections
        };

        MusicTrack musicTrack = new()
        {
            Class = "Actor_Template",
            Components =
            [
                new TrackDataHolder
                {
                    Class = "MusicTrackComponent_Template",
                    TrackData = new TrackData
                    {
                        Class = "MusicTrackData",
                        Structure = structure,
                        Path = $"world/maps/{mapNameLower}/audio/{mapNameLower}.wav",
                        Url = $"jmcs://jd-contents/{mapName}/{mapName}.ogg"
                    }
                }
            ]
        };

        return ToBytes(JsonSerializer.Serialize(musicTrack, _jsonOptions));
    }

    public object GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> clips = [];

        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;
                clips.Add(new
                {
                    __class = "MotionClip",
                    clip.Id,
                    timeline.TrackId,
                    IsActive = 1,
                    clip.StartTime,
                    move.Duration,
                    ClassifierPath = $"world/maps/{mapNameLower}/timeline/moves/{clip.MoveId}.msm",
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    timeline.CoachId,
                    MoveType = 0,
                    Color = ParseColorToRgba(move.Color),
                    MotionPlatformSpecifics = new
                    {
                        X360 = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 },
                        ORBIS = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = -0.2, HighThreshold = 0.6 },
                        DURANGO = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 }
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

                clips.Add(new
                {
                    __class = "MotionClip",
                    clip.Id,
                    timeline.TrackId,
                    IsActive = 1,
                    clip.StartTime,
                    move.Duration,
                    ClassifierPath = $"world/maps/{mapNameLower}/timeline/moves/{clip.MoveId}.gesture",
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    timeline.CoachId,
                    MoveType = 1,
                    Color = ParseColorToRgba(move.Color),
                    MotionPlatformSpecifics = new
                    {
                        X360 = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 },
                        ORBIS = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = -0.2, HighThreshold = 0.6 },
                        DURANGO = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 }
                    }
                });
            }
        }

        foreach (PictogramClip pictoClip in package.Pictograms.Clips)
        {
            clips.Add(new
            {
                __class = "PictogramClip",
                pictoClip.Id,
                TrackId = 1272115770L,
                IsActive = 1,
                pictoClip.StartTime,
                pictoClip.Duration,
                PictoPath = $"world/maps/{mapNameLower}/timeline/pictos/{pictoClip.PictogramId}.png",
                CoachCount = 4294967295u
            });
        }

        foreach (GoldEffectClip goldClip in package.GoldEffects.Clips)
        {
            clips.Add(new
            {
                __class = "GoldEffectClip",
                goldClip.Id,
                TrackId = 628418524L,
                IsActive = 1,
                goldClip.StartTime,
                goldClip.Duration,
                goldClip.EffectType
            });
        }

        var dtape = new
        {
            __class = "Tape",
            Clips = clips.OrderBy(c => ((dynamic)c).StartTime).ToList(),
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            MapName = mapNameLower,
            SoundwichEvent = ""
        };

        return ToBytes(JsonSerializer.Serialize(dtape, _jsonOptions));
    }

    public object GenerateKaraokeTape(IntermediateSongPackage package)
    {
        var clips = package.Lyrics.Clips.Select(lyric => new
        {
            __class = "KaraokeClip",
            lyric.Id,
            TrackId = 0,
            IsActive = 1,
            lyric.StartTime,
            lyric.Duration,
            Pitch = lyric.Pitch > 0 ? lyric.Pitch : 8.175798,
            lyric.Lyrics,
            IsEndOfLine = lyric.IsEndOfLine ? 1 : 0,
            ContentType = lyric.ContentType > 0 ? lyric.ContentType : 2,
            StartTimeTolerance = lyric.Tolerances?.StartTimeTolerance ?? 4,
            EndTimeTolerance = lyric.Tolerances?.EndTimeTolerance ?? 4,
            SemitoneTolerance = lyric.Tolerances?.SemitoneTolerance ?? 5
        }).OrderBy(c => c.StartTime).ToList();

        var ktape = new
        {
            __class = "Tape",
            Clips = clips,
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            package.Metadata.MapName,
            SoundwichEvent = ""
        };

        return ToBytes(JsonSerializer.Serialize(ktape, _jsonOptions));
    }

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

    #endregion

    #region Binary Generators

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

    public virtual object GenerateMenuArtActor(string textureName, string mapName)
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

    private static float[] ConvertColorToArray(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return [1.0f, 1.0f, 1.0f, 1.0f];
        try
        {
            string hex = hexColor.TrimStart('#');
            return [
                int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber) / 255.0f
            ];
        }
        catch
        {
            return [1.0f, 1.0f, 1.0f, 1.0f];
        }
    }

    private static double[] ParseColorToRgba(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor.Length < 7)
            return [1.0, 0.5, 0.5, 0.5];
        string hex = hexColor.TrimStart('#');
        return [
            1.0,
            Convert.ToInt32(hex[..2], 16) / 255.0,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0
        ];
    }

    #endregion
}
