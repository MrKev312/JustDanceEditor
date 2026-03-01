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
/// <remarks>
/// The <paramref name="EngineVersion"/> parameter is kept for interface consistency but is not used
/// since Uncooked Lua format is the same across all engine versions.
/// </remarks>
public class UncookedEngineContentGenerator(UbiArtEngineVersion EngineVersion) : IEngineContentGenerator
{
    private const long PictoTrackId = 1272115770L;
    private const long GoldEffectTrackId = 628418524L;

    private static byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);

    #region Lua Content Generators

    public byte[] GenerateSongDesc(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string artist = EscapeLuaString(package.Metadata.Artist);
        string title = EscapeLuaString(package.Metadata.Title);
        string numCoach = package.Metadata.CoachCount switch
        {
            1 => "NumCoach.Solo",
            2 => "NumCoach.Duo",
            3 => "NumCoach.Trio",
            _ => "NumCoach.Quatuor"
        };
        string difficulty = package.Metadata.Difficulty switch
        {
            1 => "SongDifficulty.Easy",
            2 => "SongDifficulty.Normal",
            3 => "SongDifficulty.Hard",
            _ => "SongDifficulty.Extreme"
        };

        StringBuilder sb = new();
        sb.AppendLine("includeReference(\"EngineData/Helpers/SongDatabase.ilu\")");
        sb.AppendLine();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("\tTAGS =");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\t{");
        sb.AppendLine("\t\t\tVAL = \"songdescmain\",");
        sb.AppendLine("\t\t},");
        sb.AppendLine("\t},");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME=\"JD_SongDescTemplate\",");
        sb.AppendLine("        JD_SongDescTemplate =");
        sb.AppendLine("        {");
        sb.AppendLine($"\t\t\tMapName\t\t\t\t\t\t=\t\t\"{mapName}\",");
        sb.AppendLine($"\t\t\tJDVersion\t\t\t\t\t=\t\t{package.Metadata.OriginalJDVersion},");
        sb.AppendLine("\t\t\tRelatedAlbums\t\t\t\t=\t\t");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tArtist\t\t\t\t\t\t=\t\t\"{artist}\",");
        sb.AppendLine($"\t\t\tTitle\t\t\t\t\t\t=\t\t\"{title}\",");
        sb.AppendLine($"\t\t\tNumCoach\t\t\t\t\t=\t\t{numCoach},");
        sb.AppendLine($"\t\t\tDifficulty\t\t\t\t\t=\t\t{difficulty},");
        sb.AppendLine("\t\t\t");
        sb.AppendLine("\t\t\t-- Game Modes");
        sb.AppendLine("\t\t\tGameModes = ");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine("\t\t\t\t{");
        sb.AppendLine("\t\t\t\t\tNAME = \"GameModeDesc\",");
        sb.AppendLine("\t\t\t\t\tGameModeDesc = ");
        sb.AppendLine("\t\t\t\t\t{");
        sb.AppendLine("\t\t\t\t\t\tmode \t\t=\tGameMode.Classic,");
        sb.AppendLine("\t\t\t\t\t\tflags\t\t=\tGameModeFlags.None,");
        sb.AppendLine("\t\t\t\t\t\tstatus\t\t=\tGameModeStatus.Available,");
        sb.AppendLine("\t\t\t\t\t},");
        sb.AppendLine("\t\t\t\t},");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t\t");
        sb.AppendLine("\t\t\t-- Default Colors");
        sb.AppendLine("\t\t\tDefaultColors = ");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine($"\t\t\t\t{{ KEY = \"lyrics\", VAL = \"{package.Metadata.LyricsColor}\" }},");
        sb.AppendLine("\t\t\t\t{ KEY = \"theme\", VAL = \"0xffffffff\" },");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t\t");
        sb.AppendLine("\t\t\t-- Audio Previews\t\t\t");
        sb.AppendLine("\t\t\tAudioPreviewFadeTime\t    =\t\t0.5,");
        sb.AppendLine("\t\t\tAudioPreviews = ");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine("\t\t\t\t{ ");
        sb.AppendLine("\t\t\t\t\tNAME = \"AudioPreview\",");
        sb.AppendLine("\t\t\t\t\tAudioPreview =");
        sb.AppendLine("\t\t\t\t\t{ ");
        sb.AppendLine("\t\t\t\t\t\tname \t\t= \t\"coverflow\",");
        sb.AppendLine($"\t\t\t\t\t\tstartbeat\t=\t{package.TimelineStructure.PreviewEntryBeat}, ");
        sb.AppendLine("\t\t\t\t\t\t-- endbeat\t=\t50,");
        sb.AppendLine("\t\t\t\t\t},");
        sb.AppendLine("\t\t\t\t},\t\t\t\t\t");
        sb.AppendLine("\t\t\t\t");
        sb.AppendLine("\t\t\t\t{ ");
        sb.AppendLine("\t\t\t\t\tNAME = \"AudioPreview\",");
        sb.AppendLine("\t\t\t\t\tAudioPreview =");
        sb.AppendLine("\t\t\t\t\t{ ");
        sb.AppendLine("\t\t\t\t\t\tname \t\t= \t\"prelobby\",");
        sb.AppendLine($"\t\t\t\t\t\tstartbeat\t=\t{package.TimelineStructure.PreviewLoopStartBeat}, ");
        sb.AppendLine($"\t\t\t\t\t\tendbeat\t=\t{package.TimelineStructure.PreviewLoopEndBeat},");
        sb.AppendLine("\t\t\t\t\t},");
        sb.AppendLine("\t\t\t\t},\t\t\t\t\t");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t\t");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");
        sb.AppendLine();

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateMusicTrack(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        // Build a MusicTrack template that includes an external .trk and references WAV
        StringBuilder sb = new();
        sb.AppendLine($"includeReference(\"world/maps/{mapNameLower}/audio/{mapName}.trk\")");
        sb.AppendLine();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("\tNAME = \"Actor_Template\",");
        sb.AppendLine("\tActor_Template =");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tCOMPONENTS =");
        sb.AppendLine("\t\t{");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine("\t\t\t\tNAME = \"MusicTrackComponent_Template\",");
        sb.AppendLine("\t\t\t\tMusicTrackComponent_Template =");
        sb.AppendLine("\t\t\t\t{");
        sb.AppendLine("\t\t\t\t\ttrackData = { MusicTrackData = { path = \"world/maps/" + mapNameLower + "/audio/" + mapName + ".wav\", structure = structure, volume = 0 } },");
        sb.AppendLine("\t\t\t\t}");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> allClips = [];
        List<object> tracks = [];

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
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"world/maps/{mapNameLower}/timeline/moves/{clip.MoveId}.msm",
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        timeline.CoachId,
                        Color = ParseColorToHex(move.Color)
                    }
                });
            }

            // Add track for this coach
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
                    PictoPath = $"world/maps/{mapNameLower}/timeline/pictos/{pictoClip.PictogramId}.png",
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

        return ToBytes(LuaTableSerializer.Serialize(danceTape));
    }

    public byte[] GenerateKaraokeTape(IntermediateSongPackage package)
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

        return ToBytes(LuaTableSerializer.Serialize(karaokeTape));
    }

    public byte[] GenerateTapeCaseTpl(string mapName, string tapeType)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string extension = tapeType == "dance" ? "dtape" : "ktape";

        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"TapeCase_Template\",");
        sb.AppendLine("        TapeCase_Template =");
        sb.AppendLine("        {");
        sb.AppendLine("          TapesRack =");
        sb.AppendLine("          {");
        sb.AppendLine("            {");
        sb.AppendLine("              TapeGroup =");
        sb.AppendLine("              {");
        sb.AppendLine("                Entries =");
        sb.AppendLine("                {");
        sb.AppendLine("                  {");
        sb.AppendLine("                    TapeEntry =");
        sb.AppendLine("                    {");
        sb.AppendLine($"                      Label = \"{tapeType}\",");
        sb.AppendLine($"                      Path = \"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_{tapeType}.{extension}\",");
        sb.AppendLine("                    },");
        sb.AppendLine("                  },");
        sb.AppendLine("                },");
        sb.AppendLine("              },");
        sb.AppendLine("            },");
        sb.AppendLine("          },");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateSequenceTpl()
    {
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"TapeCase_Template\",");
        sb.AppendLine("        TapeCase_Template =");
        sb.AppendLine("        {");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateSoundTape(string mapName)
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

        return ToBytes(LuaTableSerializer.Serialize(stape));
    }

    public byte[] GenerateAmbTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"SoundComponent_Template\",");
        sb.AppendLine("        SoundComponent_Template =");
        sb.AppendLine("        {");
        sb.AppendLine("          soundList =");
        sb.AppendLine("          {");
        sb.AppendLine("            {");
        sb.AppendLine("              SoundDescriptor_Template =");
        sb.AppendLine("              {");
        sb.AppendLine($"                name = \"amb_{mapNameLower}_intro\",");
        sb.AppendLine("                volume = 6,");
        sb.AppendLine("                category = \"amb\",");
        sb.AppendLine("                limitMode = 0,");
        sb.AppendLine("                params =");
        sb.AppendLine("                {");
        sb.AppendLine("                  SoundParams =");
        sb.AppendLine("                  {");
        sb.AppendLine("                    loop = 0,");
        sb.AppendLine("                    playMode = 1,");
        sb.AppendLine("                  },");
        sb.AppendLine("                },");
        sb.AppendLine("                files =");
        sb.AppendLine("                {");
        sb.AppendLine($"                  \"world/maps/{mapNameLower}/audio/amb/amb_{mapNameLower}_intro.wav\",");
        sb.AppendLine("                },");
        sb.AppendLine("              },");
        sb.AppendLine("            },");
        sb.AppendLine("          },");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateMainSequenceTpl(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"MasterTape_Template\",");
        sb.AppendLine("        MasterTape_Template =");
        sb.AppendLine("        {");
        sb.AppendLine("          TapesRack =");
        sb.AppendLine("          {");
        sb.AppendLine("            {");
        sb.AppendLine("              TapeGroup =");
        sb.AppendLine("              {");
        sb.AppendLine("                Entries =");
        sb.AppendLine("                {");
        sb.AppendLine("                  {");
        sb.AppendLine("                    TapeEntry =");
        sb.AppendLine("                    {");
        sb.AppendLine("                      Label = \"Master\",");
        sb.AppendLine($"                      Path = \"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tape\",");
        sb.AppendLine("                    },");
        sb.AppendLine("                  },");
        sb.AppendLine("                },");
        sb.AppendLine("              },");
        sb.AppendLine("            },");
        sb.AppendLine("          },");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateSgs()
    {
        StringBuilder sb = new();
        sb.AppendLine("settings =");
        sb.AppendLine("{");
        sb.AppendLine("  JD_MapSceneConfig =");
        sb.AppendLine("  {");
        sb.AppendLine("    Pause_Level = 6,");
        sb.AppendLine("    name = \"\",");
        sb.AppendLine("    type = 1,");
        sb.AppendLine("    musicscore = 2,");
        sb.AppendLine("    soundContext = \"\",");
        sb.AppendLine("    hud = 0,");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateGenericActor(string className, string luaPath)
    {
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor\",");
        sb.AppendLine("  Actor =");
        sb.AppendLine("  {");
        sb.AppendLine($"    LUA = \"{luaPath}\",");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine($"        NAME = \"{className}\",");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateAutodanceTape(IntermediateSongPackage package)
    {
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor_Template\",");
        sb.AppendLine("  Actor_Template =");
        sb.AppendLine("  {");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"JD_AutodanceComponent_Template\",");
        sb.AppendLine("        JD_AutodanceComponent_Template =");
        sb.AppendLine("        {");
        sb.AppendLine($"          song = \"{package.Metadata.MapName}\",");
        sb.AppendLine("          autodanceData =");
        sb.AppendLine("          {");
        sb.AppendLine("            JD_AutodanceData =");
        sb.AppendLine("            {");
        sb.AppendLine("              recording_structure =");
        sb.AppendLine("              {");
        sb.AppendLine("                JD_AutodanceRecordingStructure =");
        sb.AppendLine("                {");
        sb.AppendLine("                  records = {},");
        sb.AppendLine("                },");
        sb.AppendLine("              },");
        sb.AppendLine("              playback_events = {},");
        sb.AppendLine("            },");
        sb.AppendLine("          },");
        sb.AppendLine("        },");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");

        return ToBytes(sb.ToString());
    }

    public byte[] GenerateMainSequenceTape(IntermediateSongPackage package)
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
                    SoundSetPath = $"world/maps/{mapNameLower}/audio/amb/amb_{mapNameLower}_intro.tpl"
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

        return ToBytes(LuaTableSerializer.Serialize(tape));
    }

    #endregion

    #region XML Scene Generators (XML is the same for Uncooked, not JSON)

    public byte[] GenerateMainScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_AUDIO"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""enginedata/actortemplates/subscene.tpl"" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/audio/{mapNameLower}_audio.isc"" EMBED_SCENE=""0"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"">
				<ENUM NAME=""viewType"" SEL=""2"" />
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_CINE"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""enginedata/actortemplates/subscene.tpl"" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_cine.isc"" EMBED_SCENE=""0"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"">
				<ENUM NAME=""viewType"" SEL=""2"" />
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_TML"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""enginedata/actortemplates/subscene.tpl"" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml.isc"" EMBED_SCENE=""0"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"">
				<ENUM NAME=""viewType"" SEL=""2"" />
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName} : SongDesc"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/songdesc.tpl"">
				<COMPONENTS NAME=""JD_SongDescComponent"">
					<JD_SongDescComponent />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"">
				<sceneConfigs NAME=""JD_MapSceneConfig"">
					<JD_MapSceneConfig name="""" soundContext="""" hud=""0"">
						<ENUM NAME=""Pause_Level"" SEL=""6"" />
						<ENUM NAME=""type"" SEL=""1"" />
						<ENUM NAME=""musicscore"" SEL=""2"" />
					</JD_MapSceneConfig>
				</sceneConfigs>
			</SceneConfigs>
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateAudioScene(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""MusicTrack"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/audio/{mapNameLower}_musictrack.tpl"">
				<COMPONENTS NAME=""MusicTrackComponent"">
					<MusicTrackComponent />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateTimelineScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_dance"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""{mapNameLower}_tml_dance.act"" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl"">
				<COMPONENTS NAME=""TapeCase_Component"">
					<TapeCase_Component />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_karaoke"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""{mapNameLower}_tml_karaoke.act"" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl"">
				<COMPONENTS NAME=""TapeCase_Component"">
					<TapeCase_Component />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateCinematicsScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_MainSequence"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.act"" LUA=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl"">
				<COMPONENTS NAME=""MasterTape"">
					<MasterTape bankState=""4294967295"" />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateMenuArtScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""1"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.3 0.3"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_generic"" POS2D=""100.0 100.0"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent>
						<material>
							<GFXMaterialSerializable>
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_generic.tga"" />
								</textureSet>
							</GFXMaterialSerializable>
						</material>
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.3 0.3"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_coach_1"" POS2D=""400.0 100.0"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent>
						<material>
							<GFXMaterialSerializable>
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_coach_1.tga"" />
								</textureSet>
							</GFXMaterialSerializable>
						</material>
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.5 0.25"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_map_bkg"" POS2D=""700.0 100.0"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent>
						<material>
							<GFXMaterialSerializable>
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_map_bkg.tga"" />
								</textureSet>
							</GFXMaterialSerializable>
						</material>
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateAutodanceScene(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_Autodance"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.tpl"">
				<COMPONENTS NAME=""JD_AutodanceComponent"">
					<JD_AutodanceComponent />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateGraphScene(string mapName)
    {
        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateVideoScene(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();

        string content = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_video"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""video_player_main.act"">
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
        return ToBytes(content);
    }

    public byte[] GenerateVideoMapPreviewScene(string mapName)
    {
        // Uncooked typically doesn't need map preview scenes, but provide a basic one
        return GenerateVideoScene(mapName);
    }

    #endregion

    #region Binary/Special Generators (return empty for uncooked as these are typically cooked-only)

    public byte[] GenerateVideoPlayerActor(string mapName, bool isPreview)
    {
        // Not typically needed for Uncooked - return empty actor
        string mapNameLower = mapName.ToLowerInvariant();
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor\",");
        sb.AppendLine("  Actor =");
        sb.AppendLine("  {");
        sb.AppendLine($"    LUA = \"world/maps/{mapNameLower}/videoscoach/{mapNameLower}_video.tpl\",");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");
        return ToBytes(sb.ToString());
    }

    public byte[] GenerateMpd()
    {
        // MPD files are for cooked platforms - return empty for uncooked
        return [];
    }

    public byte[] GenerateAutodanceActor(string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor\",");
        sb.AppendLine("  Actor =");
        sb.AppendLine("  {");
        sb.AppendLine($"    LUA = \"world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.tpl\",");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"JD_AutodanceComponent\",");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");
        return ToBytes(sb.ToString());
    }

    public byte[] GenerateMenuArtActor(string textureName, string mapName)
    {
        // Menu art actors are not typically needed for uncooked
        string mapNameLower = mapName.ToLowerInvariant();
        StringBuilder sb = new();
        sb.AppendLine("params =");
        sb.AppendLine("{");
        sb.AppendLine("  NAME = \"Actor\",");
        sb.AppendLine("  Actor =");
        sb.AppendLine("  {");
        sb.AppendLine($"    LUA = \"world/maps/{mapNameLower}/menuart/actors/{textureName}.tpl\",");
        sb.AppendLine("    COMPONENTS =");
        sb.AppendLine("    {");
        sb.AppendLine("      {");
        sb.AppendLine("        NAME = \"MaterialGraphicComponent\",");
        sb.AppendLine("      },");
        sb.AppendLine("    },");
        sb.AppendLine("  },");
        sb.AppendLine("}");
        return ToBytes(sb.ToString());
    }

    #endregion

    #region Helper Methods

    private static string EscapeLuaString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string ParseColorToHex(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor.Length < 7)
            return "0xFFFF8080";
        
        // Strip leading '#' and prepend with '0xFF'
        string hex = hexColor.TrimStart('#');
        return $"0xFF{hex}";
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
