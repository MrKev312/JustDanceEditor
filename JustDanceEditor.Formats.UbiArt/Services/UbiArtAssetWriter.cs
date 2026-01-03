using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

using System.Globalization; // for invariant number formatting
using System.Text;

using Xabe.FFmpeg; // audio/video conversions

namespace JustDanceEditor.Formats.UbiArt.Services;

public static class UbiArtAssetWriter
{
    private const long PictoTrackId = 1272115770L;
    private const long GoldEffectTrackId = 628418524L;

    public static async Task ExportToUncookedAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder)
    {
        Logger.Log($"Exporting {package.Metadata.MapName} to Uncooked UbiArt...");

        // Ensure directories
        Directory.CreateDirectory(outputFolder);
        string audioFolder = Path.Combine(outputFolder, "Audio");
        string timelineFolder = Path.Combine(outputFolder, "timeline");
        string cinematicsFolder = Path.Combine(outputFolder, "cinematics");
        string pictosFolder = Path.Combine(timelineFolder, "pictos");
        string movesFolder = Path.Combine(timelineFolder, "moves", "WiiU");
        string videosFolder = Path.Combine(outputFolder, "VideosCoach");

        Directory.CreateDirectory(audioFolder);
        Directory.CreateDirectory(timelineFolder);
        Directory.CreateDirectory(cinematicsFolder);
        Directory.CreateDirectory(pictosFolder);
        Directory.CreateDirectory(movesFolder);
        Directory.CreateDirectory(videosFolder);

        // Copy assets if materializedRoot is available
        if (!string.IsNullOrEmpty(materializedRoot))
        {
            await CopyAssetsAsync(package, materializedRoot, outputFolder);
        }

        // Write SongDesc.tpl
        Logger.Log("Writing SongDesc.tpl...");
        string songDescLua = BuildSongDescTpl(package);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "songdesc.tpl"), songDescLua);

        // Write cinematics files
        Logger.Log("Writing cinematics files...");
        await WriteCinematicsAsync(package, cinematicsFolder);

        // Prepare & Write MusicTrack.tpl (ensure WAV, external .trk and AMB if needed)
        Logger.Log("Preparing and writing MusicTrack.tpl and audio assets...");
        // Ensure audio files are present and create .wav, .trk and AMB slices as needed
        await PrepareAudioForUncookedAsync(package, materializedRoot, outputFolder);

        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        string musicTrackTpl = BuildMusicTrackTpl(package.Metadata.MapName, mapNameLower);
        await File.WriteAllTextAsync(Path.Combine(audioFolder, $"{package.Metadata.MapName}_musictrack.tpl"), musicTrackTpl);

        // Write Tapes
        Logger.Log("Writing Tapes...");
        await WriteTapesAsync(package, timelineFolder);

        Logger.Log("Uncooked export completed.");
    }

    private static async Task WriteTapesAsync(IntermediateSongPackage package, string timelineFolder)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

        // Build all clips: MotionClips + PictogramClips
        List<object> allClips = [];

        // Add MotionClips
        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move);

                if (move == null)
                {
                    Logger.Log($"Warning: MoveId '{clip.MoveId}' not found in HandCoachMoves dictionary.", LogLevel.Warning);
                    continue;
                }

                string color = move.Color[1..]; // strip leading '#' from '#RRGGBB'];

                allClips.Add(new
                {
                    NAME = "MotionClip",
                    MotionClip = new
                    {
                        clip.Id,
                        timeline.TrackId,
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"world/Maps/{mapNameLower}/timeline/moves/{clip.MoveId}.msm",
                        timeline.CoachId,
                        Color = $"0xFF{color}",
                        GoldMove = clip.IsGoldMove ? 1 : 0
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
                    PictoPath = $"world/Maps/{mapNameLower}/timeline/pictos/{pictoClip.PictogramId}.png"
                }
            });
        }

        // Build Tracks section
        List<object> tracks = [];

        // Add MoveTrack for each coach (hand tracking)
        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            tracks.Add(new
            {
                NAME = "MoveTrack",
                MoveTrack = new
                {
                    Id = timeline.TrackId,
                    Name = $"Moves{timeline.CoachId + 1}",
                    timeline.CoachId
                }
            });
        }

        // Add MoveTrack for camera moves (full body tracking) if available
        foreach (MoveTimeline timeline in package.FullBodyCoachTimelines)
        {
            tracks.Add(new
            {
                NAME = "MoveTrack",
                MoveTrack = new
                {
                    Id = timeline.TrackId,
                    Name = $"CameraMoves{timeline.CoachId + 1}",
                    timeline.CoachId,
                    MoveType = 1
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
                Clips = allClips.ToArray(),
                Tracks = tracks.ToArray(),
                TapeClock = 0,
                package.Metadata.MapName
            }
        };

        await File.WriteAllTextAsync(Path.Combine(timelineFolder, $"{package.Metadata.MapName}_tml_dance.dtape"), LuaTableSerializer.Serialize(danceTape));

        // Write Karaoke Tape if exists
        if (package.Lyrics.Clips.Count > 0)
        {
            var karaokeTape = new
            {
                NAME = "Tape",
                Tape = new
                {
                    Clips = package.Lyrics.Clips.Select(c => new
                    {
                        NAME = "KaraokeClip",
                        KaraokeClip = new
                        {
                            c.Id,
                            c.StartTime,
                            c.Duration,
                            c.Lyrics,
                            c.Pitch,
                            IsEndOfLine = c.IsEndOfLine ? 1 : 0,
                            c.ContentType
                        }
                    }).ToArray()
                }
            };
            await File.WriteAllTextAsync(Path.Combine(timelineFolder, $"{package.Metadata.MapName}_tml_karaoke.ktape"), LuaTableSerializer.Serialize(karaokeTape));
        }
    }

    private static string BuildSongDescTpl(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string artist = package.Metadata.Artist.Replace("\"", "\\\"");
        string title = package.Metadata.Title.Replace("\"", "\\\"");
        string numCoach = package.Metadata.CoachCount == 1 ? "NumCoach.Solo" :
                         package.Metadata.CoachCount == 2 ? "NumCoach.Duo" :
                         package.Metadata.CoachCount == 3 ? "NumCoach.Trio" :
                         "NumCoach.Quatuor";
        string difficulty = package.Metadata.Difficulty == 1 ? "SongDifficulty.Easy" :
                           package.Metadata.Difficulty == 2 ? "SongDifficulty.Normal" :
                           package.Metadata.Difficulty == 3 ? "SongDifficulty.Hard" :
                           "SongDifficulty.Extreme";

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
        sb.AppendLine($"\t\t\t\t\t\tstartbeat\t=\t{package.TimelineStructure.PreviewEntryBeat} , ");
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

        return sb.ToString();
    }

    private static async Task WriteCinematicsAsync(IntermediateSongPackage package, string cinematicsFolder)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        // 1. Write ISC file (scene descriptor)
        string iscContent = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_MainSequence"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""world/Maps/{mapNameLower}/cinematics/{mapName}_mainsequence.act"" LUA=""world/Maps/{mapNameLower}/cinematics/{mapName}_mainsequence.tpl"">
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
        await File.WriteAllTextAsync(Path.Combine(cinematicsFolder, $"{mapName}_CINE.isc"), iscContent);

        // 2. Write ACT file (actor descriptor)
        string actContent = $@"params = 
{{
    NAME = ""Actor"", 
    Actor = 
    {{
        LUA = ""world/Maps/{mapNameLower}/cinematics/{mapName}_mainsequence.tpl"", 
        COMPONENTS = 
        {{
            
            {{
                NAME = ""MasterTape""
            }}
        }}
    }}
}}
";
        await File.WriteAllTextAsync(Path.Combine(cinematicsFolder, $"{mapName}_MainSequence.act"), actContent);

        // 3. Write TPL file (template with tape reference)
        string tplContent = $@"params =
{{
    NAME=""Actor_Template"",
    Actor_Template =
    {{
        COMPONENTS =
        {{
            {{
                NAME = ""MasterTape_Template"",
                MasterTape_Template =
                {{
                    TapesRack =
                    {{
                        {{
                            TapeGroup =
                            {{
                                Entries =
                                {{
                                    {{
                                        TapeEntry =
                                        {{
                                            Label = ""Master"",
                                            Path = ""world/Maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tape"",
                                        }},
                                    }},
                                }},
                            }},
                        }},
                    }},
                }},
            }},
        }},
    }}
}}
";
        await File.WriteAllTextAsync(Path.Combine(cinematicsFolder, $"{mapName}_MainSequence.tpl"), tplContent);

        // 4. Write TAPE file (currently empty, would contain SoundSetClips for AMB effects)
        string tapeContent = $@"params =
{{
    NAME=""Tape"",
    Tape =
    {{
        Clips =
        {{
        }},
    }}
}}
";
        await File.WriteAllTextAsync(Path.Combine(cinematicsFolder, $"{mapName}_MainSequence.tape"), tapeContent);
    }

    /// <summary>
    /// Prepare audio assets for Uncooked export:
    /// - Convert intermediate master audio to WAV (48kHz, 16-bit)
    /// - Trim the intro (based on StartBeat) and write an AMB clip (audio/amb/amb_<Map>_intro.wav)
    /// - Write master WAV (audio/<Map>.wav) starting at the start beat
    /// - Write an external .trk file containing the MusicTrack structure
    /// - Write AMB .ilu and .tpl entries under Audio/AMB
    /// </summary>
    private static async Task PrepareAudioForUncookedAsync(IntermediateSongPackage package, string? materializedRoot, string mapSubFolder)
    {
        if (string.IsNullOrEmpty(materializedRoot))
        {
            Logger.Log("No materialized root available; skipping audio preparation.", LogLevel.Warning);
            return;
        }

        string audioSource = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!File.Exists(audioSource))
        {
            Logger.Log($"Audio source not found: {audioSource}", LogLevel.Warning);
            return;
        }

        string audioFolder = Path.Combine(mapSubFolder, "audio");
        string audioAmbFolder = Path.Combine(audioFolder, "amb");
        Directory.CreateDirectory(audioFolder);
        Directory.CreateDirectory(audioAmbFolder);

        string mapName = package.Metadata.MapName;

        string tempWav = Path.Combine(Path.GetTempPath(), $"jdi_export_{Guid.NewGuid()}.wav");
        try
        {
            Logger.Log("Converting intermediate audio to WAV...");
            // Convert whatever source (likely .opus) to WAV 48kHz stereo
            IConversion conversion = FFmpeg.Conversions.New();
            // Place -i before output options so ffmpeg parses options correctly
            conversion.AddParameter($"-y -i \"{audioSource}\" -ar 48000 -ac 2");
            conversion.SetOutput(tempWav);
            conversion.SetOverwriteOutput(true);
            await conversion.Start();

            // Determine cut duration from startBeat if negative
            double cutDurationSeconds = 0.0;
            double startBeat = package.TimelineStructure.StartBeat;
            List<int> markers = package.TimelineStructure.Markers;

            if (startBeat < 0 && markers != null && markers.Count > 1)
            {
                // seconds per beat ≈ markers[1] / 48000
                double secondsPerBeat = markers[1] / 48000.0;
                cutDurationSeconds = Math.Abs(startBeat) * secondsPerBeat;
                Logger.Log($"StartBeat {startBeat}: trimming {cutDurationSeconds} seconds from start for AMB generation.", LogLevel.Info);
            }

            // Generate AMB slice if necessary
            if (cutDurationSeconds > 0.001)
            {
                string ambFileName = $"amb_{mapName}_intro.wav";
                string ambDest = Path.Combine(audioAmbFolder, ambFileName);

                Logger.Log($"Creating AMB intro slice '{ambFileName}' ({cutDurationSeconds}s)...", LogLevel.Info);
                IConversion ambConversion = FFmpeg.Conversions.New();
                ambConversion.AddParameter($"-y -i \"{tempWav}\" -t {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -ar 48000 -ac 2");
                ambConversion.SetOutput(ambDest);
                ambConversion.SetOverwriteOutput(true);
                await ambConversion.Start();

                // Write ILU and TPL entries for AMB
                string audioWorldPath = $"world/Maps/{mapName.ToLowerInvariant()}/audio/amb/{ambFileName}";
                string iluContent = $"DESCRIPTOR = \n{{\n    {{\n\t\tSoundDescriptor_Template=\n\t\t{{\n\t\t\tname=\"amb_{mapName}_intro\",  \n\t\t\tvolume=-50,\n\t\t\tcategory=\"AMB\",\n\t\t\tlimitMode=LimiterMode.RejectNew,\n\t\t\tparams=\n\t\t\t{{SoundParams={{\n\t\t\t\tnumChannels=2,\n\t\t\t\tloop=0, \n\t\t\t\tplayMode=PlayMode.Random,\n\t\t\t\trandomVolMin=0.0,\n\t\t\t\trandomVolMax=0.0,\n\t\t\t\trandomPitchMin=1.0,\n\t\t\t\trandomPitchMax=1.0,\n\t\t\t\tfadeInTime=0.0,\n\t\t\t\tfadeOutTime=0.0,\n\t\t\t}}}},\n\t\t\tfiles=\n\t\t\t{{\n\t\t\t\t{{\n\t\t\t\t\tVAL=\"{audioWorldPath}\",\n\t\t\t\t}},\n\t\t\t}},\n\t\t}}\n\t}},\n}}\n\nappendTable(component.SoundComponent_Template.soundList,DESCRIPTOR)";

                string ambIluDir = Path.Combine(mapSubFolder, "Audio", "AMB");
                Directory.CreateDirectory(ambIluDir);
                string iluFileName = $"AMB_{mapName}_Intro.ilu";
                string iluPath = Path.Combine(ambIluDir, iluFileName);
                await File.WriteAllTextAsync(iluPath, iluContent);

                string tplContent = "params=\n{\n\tNAME=\"Actor_Template\",\n\tActor_Template=\n\t{\n\t\tCOMPONENTS=\n\t\t{\n\t\t}\n\t}\n}\nincludeReference(\"world/Maps/" + mapName.ToLowerInvariant() + "/audio/amb/" + ambFileName + "\")\n";
                string tplFileName = $"AMB_{mapName}_Intro.tpl";
                string tplPath = Path.Combine(ambIluDir, tplFileName);
                await File.WriteAllTextAsync(tplPath, tplContent);

                Logger.Log($"Wrote AMB ILU and TPL to {ambIluDir}", LogLevel.Info);
            }

            // Create master WAV starting after the intro slice (or full file if no cut)
            string masterWavDest = Path.Combine(audioFolder, $"{package.Metadata.MapName}.wav");
            if (cutDurationSeconds > 0.001)
            {
                Logger.Log($"Creating trimmed master WAV (cut {cutDurationSeconds}s) -> {masterWavDest}", LogLevel.Info);
                IConversion masterConv = FFmpeg.Conversions.New();
                masterConv.AddParameter($"-y -ss {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{tempWav}\" -ar 48000 -ac 2");
                masterConv.SetOutput(masterWavDest);
                masterConv.SetOverwriteOutput(true);
                await masterConv.Start();
            }
            else
            {
                Logger.Log($"Copying full WAV to {masterWavDest}", LogLevel.Info);
                File.Copy(tempWav, masterWavDest, true);
            }

            // Write external .trk file with structure data
            string trkPath = Path.Combine(audioFolder, $"{package.Metadata.MapName}.trk");

            StringBuilder trkBuilder = new();
            trkBuilder.AppendLine("structure = { MusicTrackStructure = {");

            // markers
            trkBuilder.AppendLine("markers = {");
            foreach (int m in package.TimelineStructure.Markers)
            {
                trkBuilder.AppendLine($"    {{ VAL = {m} }},");
            }

            trkBuilder.AppendLine("},");

            // signatures
            trkBuilder.AppendLine("signatures = {");
            foreach (SignatureSegment s in package.TimelineStructure.Signatures)
            {
                string comment = s.Comment ?? string.Empty;
                string markerStr = s.Marker.ToString(CultureInfo.InvariantCulture);
                trkBuilder.AppendLine($"    {{ MusicSignature = {{ beats = {s.Beats}, marker = {markerStr}, comment = \"{comment.Replace("\"", "\\\"")}\" }} }},");
            }

            trkBuilder.AppendLine("},");

            // sections
            trkBuilder.AppendLine("sections = {");
            foreach (SectionSegment sec in package.TimelineStructure.Sections)
            {
                string comment = sec.Comment ?? string.Empty;
                string markerStr = sec.StartBeat.ToString(CultureInfo.InvariantCulture);
                trkBuilder.AppendLine($"    {{ MusicSection = {{ sectionType = {sec.SectionType}, marker = {markerStr}, comment = \"{comment.Replace("\"", "\\\"")}\" }} }},");
            }

            trkBuilder.AppendLine("},");

            // comments
            trkBuilder.AppendLine("comments = {},");

            // basic fields
            trkBuilder.AppendLine($"startBeat = {package.TimelineStructure.StartBeat},");
            trkBuilder.AppendLine($"endBeat = {package.TimelineStructure.EndBeat},");
            trkBuilder.AppendLine($"videoStartTime = {package.TimelineStructure.VideoStartOffset.ToString(CultureInfo.InvariantCulture)},");
            trkBuilder.AppendLine($"previewEntry = {package.TimelineStructure.PreviewEntryBeat},");
            trkBuilder.AppendLine($"previewLoopStart = {package.TimelineStructure.PreviewLoopStartBeat},");
            trkBuilder.AppendLine($"previewLoopEnd = {package.TimelineStructure.PreviewLoopEndBeat},");
            trkBuilder.AppendLine($"previewDuration = {package.TimelineStructure.PrevewDuration},");

            trkBuilder.AppendLine("} } ");

            await File.WriteAllTextAsync(trkPath, trkBuilder.ToString());
            Logger.Log($"Wrote track file: {trkPath}", LogLevel.Info);
        }
        finally
        {
            try
            {
                File.Delete(tempWav);
            }
            catch { }
        }
    }

    private static string BuildMusicTrackTpl(string mapName, string mapNameLower)
    {
        // Build a MusicTrack template that includes an external .trk and references WAV using world/Maps path
        return $"includeReference(\"world/Maps/{mapNameLower}/audio/{mapName}.trk\")\n\nparams =\n{{\n\tNAME = \"Actor_Template\",\n\tActor_Template =\n\t{{\n\t\tCOMPONENTS = \n\t\t{{\n\t\t\t{{\n\t\t\t\tNAME = \"MusicTrackComponent_Template\",\n\t\t\t\tMusicTrackComponent_Template =\n\t\t\t\t{{\n\t\t\t\t\ttrackData = {{ MusicTrackData = {{ path = \"world/Maps/{mapNameLower}/audio/{mapName}.wav\", structure = structure, volume = 0 }} }},\n\t\t\t\t}}\n\t\t\t}},\n\t\t}}\n\t}}\n}}\n";
    }

    private static async Task CopyAssetsAsync(IntermediateSongPackage package, string materializedRoot, string mapSubFolder)
    {
        Logger.Log($"Copying assets from materialized root: {materializedRoot}");

        // 1. Audio
        string audioSource = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        if (File.Exists(audioSource))
        {
            string ext = Path.GetExtension(audioSource);
            // For Uncooked export we must not copy .opus directly; it will be converted to WAV and trimmed.
            if (ext.Equals(".opus", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Found source audio {audioSource} (opus); skipping raw copy and will convert to WAV for Uncooked export.", LogLevel.Info);
            }
            else
            {
                string audioDest = Path.Combine(mapSubFolder, "Audio", $"{package.Metadata.MapName}{ext}");
                Directory.CreateDirectory(Path.GetDirectoryName(audioDest)!);
                File.Copy(audioSource, audioDest, true);
                Logger.Log($"Copied audio to {audioDest}");
            }
        }
        else
        {
            Logger.Log($"Audio source not found: {audioSource}", LogLevel.Warning);
        }

        // 2. Video (Highest size)
        string videoSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.VideoFolder);
        if (Directory.Exists(videoSourceDir))
        {
            string[] files = Directory.GetFiles(videoSourceDir, "*.webm");
            if (files.Length > 0)
            {
                string largest = files.OrderByDescending(f => new FileInfo(f).Length).First();
                string videoDest = Path.Combine(mapSubFolder, "VideosCoach", $"{package.Metadata.MapName}.webm");
                Directory.CreateDirectory(Path.GetDirectoryName(videoDest)!);
                File.Copy(largest, videoDest, true);
                Logger.Log($"Copied largest video to {videoDest}");
            }
            else
            {
                Logger.Log($"No .webm files found in {videoSourceDir}", LogLevel.Warning);
            }
        }
        else
        {
            Logger.Log($"Video source directory not found: {videoSourceDir}", LogLevel.Warning);
        }

        // 3. Pictograms
        string pictosSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.PictogramsFolder);
        if (Directory.Exists(pictosSourceDir))
        {
            string pictosDestDir = Path.Combine(mapSubFolder, "timeline", "pictos");
            Directory.CreateDirectory(pictosDestDir);
            string[] files = Directory.GetFiles(pictosSourceDir);
            int copied = 0;
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                string baseName = Path.GetFileNameWithoutExtension(file);
                if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    // Convert WEBP to PNG for the Uncooked export because the editor expects PNG pictograms
                    try
                    {
                        using Image img = Image.Load(file);
                        // If we have 1 coach, resize to 512x512 centered
                        if (package.Metadata.CoachCount == 1)
                        {
                            img.Mutate(x => x.Resize(new ResizeOptions
                            {
                                Size = new Size(512, 512),
                                Mode = ResizeMode.Pad,
                                Position = AnchorPositionMode.Center,
                                PadColor = Color.Transparent
                            }));
                        }

                        string destFile = Path.Combine(pictosDestDir, baseName + ".png");
                        img.Save(destFile, new PngEncoder());
                        copied++;
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Failed to convert pictogram {file} to PNG: {e.Message}", LogLevel.Warning);
                    }
                }
                else
                {
                    string destFile = Path.Combine(pictosDestDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                    copied++;
                }
            }

            Logger.Log($"Copied {copied} pictograms to {pictosDestDir}.");
        }
        else
        {
            Logger.Log($"Pictograms source directory not found: {pictosSourceDir}", LogLevel.Warning);
        }

        // 4. MSMs (Moves)
        string movesSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.MovesFolder);
        if (Directory.Exists(movesSourceDir))
        {
            string movesDestDir = Path.Combine(mapSubFolder, "timeline", "moves", "WiiU");
            Directory.CreateDirectory(movesDestDir);
            string[] files = Directory.GetFiles(movesSourceDir, "*.msm");
            foreach (string file in files)
            {
                string destFile = Path.Combine(movesDestDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            Logger.Log($"Copied {files.Length} MSMs.");
        }
        else
        {
            Logger.Log($"Moves source directory not found: {movesSourceDir}", LogLevel.Warning);
        }
    }
}