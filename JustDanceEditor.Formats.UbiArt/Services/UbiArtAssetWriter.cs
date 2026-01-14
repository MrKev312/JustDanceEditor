using JustDanceEditor.Audio;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Tapes;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Globalization; // for invariant number formatting
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using TextureConverter.TextureType;

using Xabe.FFmpeg; // audio/video conversions

namespace JustDanceEditor.Formats.UbiArt.Services;

/// <summary>
/// Service for exporting UbiArt assets from intermediate package format.
/// </summary>
public sealed partial class UbiArtAssetWriter(ILogger<UbiArtAssetWriter> logger) : IUbiArtAssetWriter
{
	private const long PictoTrackId = 1272115770L;
	private const long GoldEffectTrackId = 628418524L;

	private readonly ILogger<UbiArtAssetWriter> _logger = logger;

	/// <summary>
	/// Exports a song package to UbiArt format.
	/// Routes to uncooked (Lua) or cooked (JSON + .ckd) based on platform.
	/// </summary>
	public async Task ExportAsync(
		IntermediateSongPackage package,
		string? materializedRoot,
		string outputFolder,
		UbiArtPlatform platform,
		UbiArtEngineVersion engineVersion,
		IUbiArtLayout? layout = null,
		IFileSystem? io = null)
	{
		if (platform == UbiArtPlatform.Uncooked)
		{
			await ExportToUncookedInternalAsync(package, materializedRoot, outputFolder, layout, platform, engineVersion, io);
		}
		else
		{
			await ExportToCookedInternalAsync(package, materializedRoot, outputFolder, platform, engineVersion, layout, io);
		}
	}

	private async Task ExportToUncookedInternalAsync(
		IntermediateSongPackage package,
		string? materializedRoot,
		string outputFolder,
		IUbiArtLayout? layout = null,
		UbiArtPlatform platform = UbiArtPlatform.Uncooked,
		UbiArtEngineVersion engineVersion = UbiArtEngineVersion.JD2022,
		IFileSystem? io = null)
	{
		layout ??= new UbiArtLayoutResolver();

		IFileSystem iofs = io ?? new SystemFileSystem();

		_logger.LogInformation("Exporting {MapName} to Uncooked UbiArt...", package.Metadata.MapName);

		// Ensure directories
		iofs.CreateDirectory(outputFolder);

		string mapWorldRelative = layout.GetMapWorldFolder(outputFolder, package.Metadata.MapName, platform, engineVersion);
		string mapWorldFolder = iofs.Combine(outputFolder, mapWorldRelative);

		string audioFolder = iofs.Combine(outputFolder, layout.GetAudioFolder(outputFolder, package.Metadata.MapName, platform, engineVersion));
		string timelineFolder = iofs.Combine(outputFolder, layout.GetTimelineFolder(outputFolder, package.Metadata.MapName, platform, engineVersion));
		_ = iofs.Combine(outputFolder, layout.GetTimelineFolder(outputFolder, package.Metadata.MapName, platform, engineVersion), "..", "cinematics");
		// cinematics folder may be alongside timeline in some layouts; ensure path
		string cinematicsFolder = iofs.Combine(outputFolder, layout.GetMapWorldFolder(outputFolder, package.Metadata.MapName, platform, engineVersion), "cinematics");
		string pictosFolder = iofs.Combine(outputFolder, layout.GetPictosFolder(outputFolder, package.Metadata.MapName, platform, engineVersion));
		string movesFolder = iofs.Combine(outputFolder, layout.GetMovesFolder(outputFolder, package.Metadata.MapName, platform, engineVersion));
		string videosFolder = iofs.Combine(outputFolder, layout.GetMediaFolder(outputFolder, package.Metadata.MapName, platform, engineVersion), "VideosCoach");

		iofs.CreateDirectory(audioFolder);
		iofs.CreateDirectory(timelineFolder);
		iofs.CreateDirectory(cinematicsFolder);
		iofs.CreateDirectory(pictosFolder);
		iofs.CreateDirectory(movesFolder);
		// VideosCoach folder is created only if videos exist, not unconditionally

		// Copy assets if materializedRoot is available
		if (!string.IsNullOrEmpty(materializedRoot))
		{
			await CopyAssetsAsync(package, materializedRoot, mapWorldFolder, iofs);
		}

		// Write SongDesc.tpl
		_logger.LogInformation("Writing SongDesc.tpl...");
		string songDescLua = BuildSongDescTpl(package);
		await iofs.WriteAllTextAsync(iofs.Combine(mapWorldFolder, "songdesc.tpl"), songDescLua);

		// Prepare & Write MusicTrack.tpl (ensure WAV, external .trk and AMB if needed)
		_logger.LogInformation("Preparing and writing MusicTrack.tpl and audio assets...");
		// Ensure audio files are present and create .wav, .trk and AMB slices as needed
		await PrepareAudioForUncookedAsync(package, materializedRoot, mapWorldFolder, iofs);

		// Write cinematics files (depends on AMB intro generated above)
		_logger.LogInformation("Writing cinematics files...");
		await WriteCinematicsAsync(package, cinematicsFolder, layout, platform, engineVersion, iofs);

		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
		string musicTrackTpl = BuildMusicTrackTpl(package.Metadata.MapName, mapNameLower);
		await iofs.WriteAllTextAsync(iofs.Combine(audioFolder, $"{package.Metadata.MapName}_musictrack.tpl"), musicTrackTpl);

		// Write Tapes
		_logger.LogInformation("Writing Tapes...");
		await WriteTapesAsync(package, timelineFolder, layout, platform, engineVersion, iofs);

		_logger.LogInformation("Uncooked export completed.");
	}

	private async Task WriteTapesAsync(IntermediateSongPackage package, string timelineFolder, IUbiArtLayout layout, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IFileSystem io)
	{
		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

		// Build all clips: MotionClips + PictogramClips
		List<object> allClips = [];

		string movesRelative = layout.GetMovesFolder(string.Empty, mapNameLower, platform, engineVersion).Replace(Path.DirectorySeparatorChar, '/');
		// For dtape/tape files, don't include the WiiU subfolder in the path
		string movesRelativeForTape = movesRelative.Replace("/WiiU", "").Replace("\\WiiU", "");
		string pictosRelative = layout.GetPictosFolder(string.Empty, mapNameLower, platform, engineVersion).Replace(Path.DirectorySeparatorChar, '/');

		// Add MotionClips
		foreach (MoveTimeline timeline in package.CoachTimelines)
		{
			foreach (MoveClip clip in timeline.Clips)
			{
				package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move);

				if (move == null)
				{
					_logger.LogWarning("MoveId '{MoveId}' not found in HandCoachMoves dictionary.", clip.MoveId);
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
						ClassifierPath = $"{movesRelativeForTape}/{clip.MoveId}.msm",
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
					PictoPath = $"{pictosRelative}/{pictoClip.PictogramId}.png"
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

		await io.WriteAllTextAsync(io.Combine(timelineFolder, $"{package.Metadata.MapName}_tml_dance.dtape"), LuaTableSerializer.Serialize(danceTape));

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
			await io.WriteAllTextAsync(io.Combine(timelineFolder, $"{package.Metadata.MapName}_tml_karaoke.ktape"), LuaTableSerializer.Serialize(karaokeTape));
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

	private static async Task WriteCinematicsAsync(IntermediateSongPackage package, string cinematicsFolder, IUbiArtLayout layout, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IFileSystem io)
	{
		string mapName = package.Metadata.MapName;
		string mapNameLower = mapName.ToLowerInvariant();
		string baseRelative = layout.GetMapWorldFolder(string.Empty, mapName, platform, engineVersion).Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');

		// 1. Write ISC file (scene descriptor)
		string iscContent = $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""55299"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_MainSequence"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE=""{baseRelative}/cinematics/{mapName}_mainsequence.act"" LUA=""{baseRelative}/cinematics/{mapName}_mainsequence.tpl"">
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
		await io.WriteAllTextAsync(io.Combine(cinematicsFolder, $"{mapName}_CINE.isc"), iscContent);

		// 2. Write ACT file (actor descriptor)
		string actContent = $@"params = 
{{
	NAME = ""Actor"", 
	Actor = 
	{{
		LUA = ""{baseRelative}/cinematics/{mapName}_mainsequence.tpl"", 
		COMPONENTS = 
		{{
			
			{{
				NAME = ""MasterTape""
			}}
		}}
	}}
}}
";
		await io.WriteAllTextAsync(io.Combine(cinematicsFolder, $"{mapName}_MainSequence.act"), actContent);

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
											Path = ""{baseRelative}/cinematics/{mapNameLower}_mainsequence.tape"",
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
		await io.WriteAllTextAsync(io.Combine(cinematicsFolder, $"{mapName}_MainSequence.tpl"), tplContent);

		// 4. Write TAPE file (add SoundSetClip for AMB intro if present)
		// Determine map root from cinematics folder (parent directory)
		string? mapRoot = Path.GetDirectoryName(cinematicsFolder);
		string ambWavPath = mapRoot != null ? io.Combine(mapRoot, "audio", "amb", $"amb_{mapName}_intro.wav") : string.Empty;
		string ambTplPathWorld = $"world/Maps/{mapName.ToLowerInvariant()}/Audio/AMB/AMB_{mapName}_Intro.tpl";

		string tapeContent;
		if (!string.IsNullOrWhiteSpace(ambWavPath) && io.FileExists(ambWavPath))
		{
			// Create a deterministic track id and clip id based on map name
			byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(mapName));
			long trackId = BitConverter.ToUInt32(hash, 0);
			long clipId = BitConverter.ToUInt32(hash, 4);

			int startBeat = package.TimelineStructure.StartBeat;
			// Convert beats to internal clip time units (24 units per beat)
			int startTime = startBeat * 24;
			int duration = Math.Abs(startBeat) * 24;

			var clips = new[]
			{
				new
				{
					NAME = "SoundSetClip",
					SoundSetClip = new
					{
						Id = clipId,
						TrackId = trackId,
						StartTime = startTime,
						Duration = duration,
						SoundSetPath = ambTplPathWorld
					}
				}
			};

			var tracks = new[]
			{
				new
				{
					NAME = "TapeTrack",
					TapeTrack = new
					{
						id = trackId,
						name = "SOUND"
					}
				}
			};

			var tapeObj = new
			{
				NAME = "Tape",
				Tape = new
				{
					Clips = clips,
					Tracks = tracks,
					TapeClock = 0
				}
			};

			tapeContent = LuaTableSerializer.Serialize(tapeObj);
		}
		else
		{
			tapeContent = $@"params =
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
		}

		await io.WriteAllTextAsync(io.Combine(cinematicsFolder, $"{mapName}_MainSequence.tape"), tapeContent);
	}

	/// <summary>
	/// Prepare audio assets for Uncooked export:
	/// - Convert intermediate master audio to WAV (48kHz, 16-bit)
	/// - Trim the intro (based on StartBeat) and write an AMB clip (audio/amb/amb_<Map>_intro.wav)
	/// - Write master WAV (audio/<Map>.wav) starting at the start beat
	/// - Write an external .trk file containing the MusicTrack structure
	/// - Write AMB .ilu and .tpl entries under Audio/AMB
	/// </summary>
	private async Task PrepareAudioForUncookedAsync(IntermediateSongPackage package, string? materializedRoot, string mapSubFolder, IFileSystem io)
	{
		if (string.IsNullOrWhiteSpace(materializedRoot))
		{
			_logger.LogWarning("No materialized root available; skipping audio preparation.");
			return;
		}

		string audioSource = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
		if (!io.FileExists(audioSource))
		{
			_logger.LogWarning("Audio source not found: {AudioSource}", audioSource);
			return;
		}

		string audioFolder = io.Combine(mapSubFolder, "audio");
		string audioAmbFolder = io.Combine(audioFolder, "amb");
		io.CreateDirectory(audioFolder);
		io.CreateDirectory(audioAmbFolder);

		string mapName = package.Metadata.MapName;

		string tempWav = io.Combine(io.GetTempPath(), $"jdi_export_{Guid.NewGuid()}.wav");
		try
		{
			_logger.LogInformation("Converting intermediate audio to WAV...");
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
				cutDurationSeconds = markers[(int)Math.Abs(startBeat)] / 48000.0;
				_logger.LogInformation("StartBeat {StartBeat}: trimming {CutSeconds} seconds from start for AMB generation.", startBeat, cutDurationSeconds);
			}

			// Generate AMB slice if necessary
			if (cutDurationSeconds > 0.001)
			{
				string ambFileName = $"amb_{mapName}_intro.wav";
				string ambDest = io.Combine(audioAmbFolder, ambFileName);

				_logger.LogInformation("Creating AMB intro slice '{AmbFile}' ({CutSeconds}s)...", ambFileName, cutDurationSeconds);
				IConversion ambConversion = FFmpeg.Conversions.New();
				ambConversion.AddParameter($"-y -i \"{tempWav}\" -t {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -ar 48000 -ac 2");
				ambConversion.SetOutput(ambDest);
				ambConversion.SetOverwriteOutput(true);
				await ambConversion.Start();

				// Write ILU and TPL entries for AMB
				string audioWorldPath = $"world/Maps/{mapName.ToLowerInvariant()}/audio/amb/{ambFileName}";
				string iluContent = $"DESCRIPTOR = \n{{\n    {{\n\t\tSoundDescriptor_Template=\n\t\t{{\n\t\t\tname=\"amb_{mapName}_intro\",  \n\t\t\tvolume=-50,\n\t\t\tcategory=\"AMB\",\n\t\t\tlimitMode=LimiterMode.RejectNew,\n\t\t\tparams=\n\t\t\t{{SoundParams={{\n\t\t\t\tnumChannels=2,\n\t\t\t\tloop=0, \n\t\t\t\tplayMode=PlayMode.Random,\n\t\t\t\trandomVolMin=0.0,\n\t\t\t\trandomVolMax=0.0,\n\t\t\t\trandomPitchMin=1.0,\n\t\t\t\trandomPitchMax=1.0,\n\t\t\t\tfadeInTime=0.0,\n\t\t\t\tfadeOutTime=0.0,\n\t\t\t}}}},\n\t\t\tfiles=\n\t\t\t{{\n\t\t\t\t{{\n\t\t\t\t\tVAL=\"{audioWorldPath}\",\n\t\t\t\t}},\n\t\t\t}},\n\t\t}}\n\t}},\n}}\n\nappendTable(component.SoundComponent_Template.soundList,DESCRIPTOR)";

				string ambIluDir = io.Combine(mapSubFolder, "Audio", "AMB");
				io.CreateDirectory(ambIluDir);
				string iluFileName = $"AMB_{mapName}_Intro.ilu";
				string iluPath = io.Combine(ambIluDir, iluFileName);
				await io.WriteAllTextAsync(iluPath, iluContent);

				string tplContent = "params=\n{\n\tNAME=\"Actor_Template\",\n\tActor_Template=\n\t{\n\t\tCOMPONENTS=\n\t\t{\n\t\t}\n\t}\n}\nincludeReference(\"world/Maps/" + mapName.ToLowerInvariant() + "/audio/amb/" + ambFileName + "\")\n";
				string tplFileName = $"AMB_{mapName}_Intro.tpl";
				string tplPath = io.Combine(ambIluDir, tplFileName);
				await io.WriteAllTextAsync(tplPath, tplContent);

				_logger.LogInformation("Wrote AMB ILU and TPL to {AmbIluDir}", ambIluDir);
			}

			// Create master WAV starting after the intro slice (or full file if no cut)
			string masterWavDest = io.Combine(audioFolder, $"{package.Metadata.MapName}.wav");
			if (cutDurationSeconds > 0.001)
			{
				_logger.LogInformation("Creating trimmed master WAV (cut {CutSeconds}s) -> {MasterWavDest}", cutDurationSeconds, masterWavDest);
				IConversion masterConv = FFmpeg.Conversions.New();
				masterConv.AddParameter($"-y -ss {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{tempWav}\" -ar 48000 -ac 2");
				masterConv.SetOutput(masterWavDest);
				masterConv.SetOverwriteOutput(true);
				await masterConv.Start();
			}
			else
			{
				_logger.LogInformation("Copying full WAV to {MasterWavDest}", masterWavDest);
				io.Copy(tempWav, masterWavDest, true);
			}

			// Write external .trk file with structure data
			string trkPath = io.Combine(audioFolder, $"{package.Metadata.MapName}.trk");

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
				trkBuilder.AppendLine($"    {{ MusicSection = {{ sectionType = {(int)sec.SectionType}, marker = {markerStr}, comment = \"{comment.Replace("\"", "\\\"")}\" }} }},");
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

			await io.WriteAllTextAsync(trkPath, trkBuilder.ToString());
			_logger.LogInformation("Wrote track file: {TrackPath}", trkPath);
		}
		finally
		{
			try
			{
				io.DeleteFile(tempWav);
			}
			catch { }
		}
	}

	private static string BuildMusicTrackTpl(string mapName, string mapNameLower)
	{
		// Build a MusicTrack template that includes an external .trk and references WAV using world/Maps path
		return $"includeReference(\"world/Maps/{mapNameLower}/audio/{mapName}.trk\")\n\nparams =\n{{\n\tNAME = \"Actor_Template\",\n\tActor_Template =\n\t{{\n\t\tCOMPONENTS = \n\t\t{{\n\t\t\t{{\n\t\t\t\tNAME = \"MusicTrackComponent_Template\",\n\t\t\t\tMusicTrackComponent_Template =\n\t\t\t\t{{\n\t\t\t\t\ttrackData = {{ MusicTrackData = {{ path = \"world/Maps/{mapNameLower}/audio/{mapName}.wav\", structure = structure, volume = 0 }} }},\n\t\t\t\t}}\n\t\t\t}},\n\t\t}}\n\t}}\n}}\n";
	}

	private async Task CopyAssetsAsync(IntermediateSongPackage package, string materializedRoot, string mapSubFolder, IFileSystem io)
	{
		_logger.LogInformation("Copying assets from materialized root: {MaterializedRoot}", materializedRoot);

		// 1. Audio
		string audioSource = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
		if (io.FileExists(audioSource))
		{
			string ext = Path.GetExtension(audioSource);
			// For Uncooked export we must not copy .opus directly; it will be converted to WAV and trimmed.
			if (ext.Equals(".opus", StringComparison.OrdinalIgnoreCase))
			{
				_logger.LogInformation("Found source audio {AudioSource} (opus); skipping raw copy and will convert to WAV for Uncooked export.", audioSource);
			}
			else
			{
				string audioDest = io.Combine(mapSubFolder, "Audio", $"{package.Metadata.MapName}{ext}");
				io.CreateDirectory(Path.GetDirectoryName(audioDest)!);
				io.Copy(audioSource, audioDest, true);
				_logger.LogInformation("Copied audio to {AudioDest}", audioDest);
			}
		}
		else
		{
			_logger.LogWarning("Audio source not found: {AudioSource}", audioSource);
		}

		// 2. Video (Highest size)
		string videoSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.VideoFolder);
		if (io.DirectoryExists(videoSourceDir))
		{
			string[] files = io.GetFiles(videoSourceDir, "*.webm");
			if (files.Length > 0)
			{
				string largest = files.OrderByDescending(f => new FileInfo(f).Length).First();
				string videoDest = io.Combine(mapSubFolder, "VideosCoach", $"{package.Metadata.MapName}.webm");
				io.CreateDirectory(Path.GetDirectoryName(videoDest)!);
				io.Copy(largest, videoDest, true);
				_logger.LogInformation("Copied largest video to {VideoDest}", videoDest);
			}
			else
			{
				_logger.LogWarning("No .webm files found in {VideoSourceDir}", videoSourceDir);
			}
		}
		else
		{
			_logger.LogWarning("Video source directory not found: {VideoSourceDir}", videoSourceDir);
		}

		// 3. Pictograms
		string pictosSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.PictogramsFolder);
		if (io.DirectoryExists(pictosSourceDir))
		{
			string pictosDestDir = io.Combine(mapSubFolder, "timeline", "pictos");
			io.CreateDirectory(pictosDestDir);
			string[] files = io.GetFiles(pictosSourceDir);
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

						string destFile = io.Combine(pictosDestDir, baseName + ".png");
						img.Save(destFile, new PngEncoder());
						copied++;
					}
					catch (Exception e)
					{
						_logger.LogWarning(e, "Failed to convert pictogram {File} to PNG: {Message}", file, e.Message);
					}
				}
				else
				{
					string destFile = io.Combine(pictosDestDir, Path.GetFileName(file));
					io.Copy(file, destFile, true);
					copied++;
				}
			}

			_logger.LogInformation("Copied {Copied} pictograms to {Dest}", copied, pictosDestDir);
		}
		else
		{
			_logger.LogWarning("Pictograms source directory not found: {SourceDir}", pictosSourceDir);
		}

		// 4. MSMs (Moves)
		string movesSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.MovesFolder);
		if (io.DirectoryExists(movesSourceDir))
		{
			string movesDestDir = io.Combine(mapSubFolder, "timeline", "moves", "WiiU");
			io.CreateDirectory(movesDestDir);
			string[] files = io.GetFiles(movesSourceDir, "*.msm");
			foreach (string file in files)
			{
				string destFile = io.Combine(movesDestDir, Path.GetFileName(file));
				io.Copy(file, destFile, true);
			}

			_logger.LogInformation("Copied {Count} MSMs.", files.Length);
		}
		else
		{
			_logger.LogWarning("Moves source directory not found: {MovesSourceDir}", movesSourceDir);
		}

		// 4a. MenuArt Textures (TGA)
		try
		{
			string menuTexturesDir = io.Combine(mapSubFolder, "menuart", "textures");
			io.CreateDirectory(menuTexturesDir);

			// Coaches (up to 4)
			string coachesSourceDir = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.CoachesFolder);
			if (io.DirectoryExists(coachesSourceDir))
			{
				string[] coachFiles = [.. io.GetFiles(coachesSourceDir)
					.Where(f => CoachMatching().IsMatch(Path.GetFileName(f)))
					.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];
				int coachIndex = 1;
				foreach (string file in coachFiles)
				{
					try
					{
						using Image<Bgra32> coachImg = Image.Load<Bgra32>(file);
						string dest = io.Combine(menuTexturesDir, $"{package.Metadata.MapName}_Coach_{coachIndex}.tga");
						SaveAsTga(coachImg, dest, io);
						coachIndex++;
						if (coachIndex > 4)
							break;
					}
					catch (Exception e)
					{
						_logger.LogWarning(e, "Failed to convert coach {File} to TGA: {Message}", file, e.Message);
					}
				}
			}

			// Cover / Album coach
			string coverSource = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.CoverFile);
			if (io.FileExists(coverSource))
			{
				try
				{
					using Image<Bgra32> coverImg = Image.Load<Bgra32>(coverSource);
					string dest = io.Combine(menuTexturesDir, $"{package.Metadata.MapName}_Cover_Generic.tga");
					SaveAsTga(coverImg, dest, io);
				}
				catch (Exception e)
				{
					_logger.LogWarning(e, "Failed to convert cover to TGA: {Message}", e.Message);
				}
			}

			// Background (map_bkg)
			string coachesBg = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile);
			if (io.FileExists(coachesBg))
			{
				try
				{
					using Image<Bgra32> bg = Image.Load<Bgra32>(coachesBg);
					string dest = io.Combine(menuTexturesDir, $"{package.Metadata.MapName}_map_bkg.tga");
					SaveAsTga(bg, dest, io);
				}
				catch (Exception e)
				{
					_logger.LogWarning(e, "Failed to convert background to TGA: {Message}", e.Message);
				}
			}

			_logger.LogInformation("Exported MenuArt textures to {Dir}", menuTexturesDir);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Failed to export MenuArt textures: {Message}", e.Message);
		}
	}

	private static void SaveAsTga(Image<Bgra32> image, string destination, IFileSystem io)
	{
		io.CreateDirectory(Path.GetDirectoryName(destination)!);
		using FileStream fs = File.Open(destination, FileMode.Create, FileAccess.Write);

		int width = image.Width;
		int height = image.Height;

		byte[] header = new byte[18];
		header[2] = 2; // uncompressed true-color image
		header[12] = (byte)(width & 0xFF);
		header[13] = (byte)((width >> 8) & 0xFF);
		header[14] = (byte)(height & 0xFF);
		header[15] = (byte)((height >> 8) & 0xFF);
		header[16] = 32; // bits per pixel
		header[17] = 0x20 | 8; // top-left origin + 8 bits of alpha

		fs.Write(header, 0, header.Length);

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Bgra32 p = image[x, y];
				fs.WriteByte(p.B);
				fs.WriteByte(p.G);
				fs.WriteByte(p.R);
				fs.WriteByte(p.A);
			}
		}
	}

	[GeneratedRegex("^coach_\\d{1,2}\\.webp$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-NL")]
	private static partial Regex CoachMatching();

	private async Task ExportToCookedInternalAsync(
		IntermediateSongPackage package,
		string? materializedRoot,
		string outputFolder,
		UbiArtPlatform platform,
		UbiArtEngineVersion engineVersion,
		IUbiArtLayout? layout = null,
		IFileSystem? io = null)
	{
		layout ??= new UbiArtLayoutResolver();
		IFileSystem iofs = io ?? new SystemFileSystem();

		// Use lowercase for all folder names in cooked format
		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
		string platformLower = platform.ToString().ToLowerInvariant();

		_logger.LogInformation("Exporting {MapName} to Cooked UbiArt ({Platform}, {Version})...", mapNameLower, platform, engineVersion);

		// Create the cache structure: cache/itf_cooked/{platform}/world/maps/{songname}
		string cookedRoot = iofs.Combine(outputFolder, "cache", "itf_cooked", platformLower);
		string cookedMapFolder = iofs.Combine(cookedRoot, "world", "maps", mapNameLower);
		string audioFolder = iofs.Combine(cookedMapFolder, "audio");
		string audioAmbFolder = iofs.Combine(audioFolder, "amb");
		string timelineFolder = iofs.Combine(cookedMapFolder, "timeline");
		string cinematicsFolder = iofs.Combine(cookedMapFolder, "cinematics");
		string pictosFolder = iofs.Combine(cookedMapFolder, "timeline", "pictos");
		string menuartFolder = iofs.Combine(cookedMapFolder, "menuart");
		string menuartTexturesFolder = iofs.Combine(menuartFolder, "textures");
		string menuartActorsFolder = iofs.Combine(menuartFolder, "actors");
		string graphFolder = iofs.Combine(cookedMapFolder, "graph");
		string autodanceFolder = iofs.Combine(cookedMapFolder, "autodance");
		string videosCoachFolder = iofs.Combine(cookedMapFolder, "videoscoach");
        // The uncooked map folder for assets that go directly into world/maps/{songname}
        string mapFolder = iofs.Combine(outputFolder, "world", "maps", mapNameLower);

		iofs.CreateDirectory(cookedMapFolder);
		iofs.CreateDirectory(audioFolder);
		iofs.CreateDirectory(audioAmbFolder);
		iofs.CreateDirectory(timelineFolder);
		iofs.CreateDirectory(cinematicsFolder);
		iofs.CreateDirectory(pictosFolder);
		iofs.CreateDirectory(menuartFolder);
		iofs.CreateDirectory(menuartTexturesFolder);
		iofs.CreateDirectory(menuartActorsFolder);
		iofs.CreateDirectory(graphFolder);
		iofs.CreateDirectory(autodanceFolder);
		iofs.CreateDirectory(videosCoachFolder);
		iofs.CreateDirectory(mapFolder);

		// Copy assets with proper cooked structure
		if (!string.IsNullOrEmpty(materializedRoot))
		{
			await CopyCookedAssetsAsync(package, materializedRoot, cookedMapFolder, mapFolder, iofs);
		}

		// Write all cooked files with .ckd extension and JSON format
		await WriteCookedFilesAsync(package, cookedMapFolder, platform, engineVersion, layout, iofs);

		_logger.LogInformation("Cooked export completed.");
	}

	/// <summary>
	/// Copies assets for cooked export with proper folder structure.
	/// Converts images to XTX format with TEX wrapper and audio to RAKI Opus format.
	/// </summary>
	private async Task CopyCookedAssetsAsync(IntermediateSongPackage package, string materializedRoot, string cookedMapFolder, string mapFolder, IFileSystem io)
	{
		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
		
		// Copy moves to world/maps/{songname}/timeline/moves/wiiu/
		string movesSource = io.Combine(materializedRoot, "assets", "moves");
		if (io.DirectoryExists(movesSource))
		{
			foreach (string moveFile in io.GetFiles(movesSource, "*.msm"))
			{
				string fileName = Path.GetFileName(moveFile).ToLowerInvariant();
				string destPath = io.Combine(mapFolder, "timeline", "moves", "wiiu", fileName);
				io.CreateDirectory(Path.GetDirectoryName(destPath)!);
				io.Copy(moveFile, destPath, true);
			}
		}

		// Convert menuart textures to cooked XTX format (.tga.ckd)
		string coachesSource = io.Combine(materializedRoot, "assets", "coaches");
		string menuartTexturesFolder = io.Combine(cookedMapFolder, "menuart", "textures");
		if (io.DirectoryExists(coachesSource))
		{
			foreach (string file in io.GetFiles(coachesSource))
			{
				try
				{
					string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
					// Convert coach names: coach_01 -> {mapname}_coach_1
					if (fileName.StartsWith("coach_"))
					{
						string coachNum = fileName.Replace("coach_0", "").Replace("coach_", "");
						fileName = $"{mapNameLower}_coach_{coachNum}";
					}
					else if (fileName == "coachesbackground")
					{
						fileName = $"{mapNameLower}_map_bkg";
					}

					string destPath = io.Combine(menuartTexturesFolder, $"{fileName}.tga.ckd");
					
					using Image<Bgra32> image = Image.Load<Bgra32>(file);
					WriteCookedTexture(image, destPath, io);
					_logger.LogDebug("Cooked texture: {FileName}", fileName);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to cook texture {File}, copying as-is", file);
					string destPath = io.Combine(menuartTexturesFolder, $"{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}.tga.ckd");
					io.Copy(file, destPath, true);
				}
			}
		}

		// Convert cover assets to cooked XTX format
		string coverSource = io.Combine(materializedRoot, "assets", "coverAssets");
		if (io.DirectoryExists(coverSource))
		{
			foreach (string file in io.GetFiles(coverSource))
			{
				try
				{
					string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
					if (fileName == "cover")
					{
						fileName = $"{mapNameLower}_cover_generic";
					}

					string destPath = io.Combine(menuartTexturesFolder, $"{fileName}.tga.ckd");
					
					using Image<Bgra32> image = Image.Load<Bgra32>(file);
					WriteCookedTexture(image, destPath, io);
					_logger.LogDebug("Cooked cover texture: {FileName}", fileName);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to cook cover {File}, copying as-is", file);
					string destPath = io.Combine(menuartTexturesFolder, $"{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}.tga.ckd");
					io.Copy(file, destPath, true);
				}
			}
		}

		// Convert pictograms to cooked XTX format (.png.ckd)
		string pictosSource = io.Combine(materializedRoot, "assets", "pictograms");
		string pictosFolder = io.Combine(cookedMapFolder, "timeline", "pictos");
		if (io.DirectoryExists(pictosSource))
		{
			foreach (string file in io.GetFiles(pictosSource))
			{
				try
				{
					string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
					string destPath = io.Combine(pictosFolder, $"{fileName}.png.ckd");
					
					using Image<Bgra32> image = Image.Load<Bgra32>(file);
					WriteCookedTexture(image, destPath, io);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to cook pictogram {File}, copying as-is", file);
					string destPath = io.Combine(pictosFolder, $"{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}.png.ckd");
					io.Copy(file, destPath, true);
				}
			}

			_logger.LogInformation("Cooked {Count} pictograms", io.GetFiles(pictosSource).Length);
		}

		// Copy video (largest webm file) to videoscoach folder
		string videoSourceDir = io.Combine(materializedRoot, "assets", "video");
		if (io.DirectoryExists(videoSourceDir))
		{
			string[] files = io.GetFiles(videoSourceDir, "*.webm");
			if (files.Length > 0)
			{
				string largest = files.OrderByDescending(f => new FileInfo(f).Length).First();
				string videosCoachFolder = io.Combine(mapFolder, "videoscoach");
				io.CreateDirectory(videosCoachFolder);
				string videoDest = io.Combine(videosCoachFolder, $"{mapNameLower}.vp9.720.webm");
				io.Copy(largest, videoDest, true);
				_logger.LogInformation("Copied largest video to {VideoDest}", videoDest);
			}
			else
			{
				_logger.LogWarning("No .webm files found in {VideoSourceDir}", videoSourceDir);
			}
		}
		else
		{
			_logger.LogWarning("Video source directory not found: {VideoSourceDir}", videoSourceDir);
		}

		// Convert audio files to RAKI Opus format (.wav.ckd)
		// Split AMB intro based on StartBeat (same logic as uncooked export)
		string audioSource = io.Combine(materializedRoot, "assets", "audio");
		string audioFolder = io.Combine(cookedMapFolder, "audio");
		string audioAmbFolder = io.Combine(audioFolder, "amb");
		
		// Find master audio file
		string? masterAudioFile = null;
		if (io.DirectoryExists(audioSource))
		{
			foreach (string file in io.GetFiles(audioSource))
			{
				string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
				if (fileName == "master")
				{
					masterAudioFile = file;
					break;
				}
			}
		}

		if (masterAudioFile != null)
		{
			// Calculate AMB cut duration from StartBeat (same as uncooked)
			double cutDurationSeconds = 0.0;
			double startBeat = package.TimelineStructure.StartBeat;
			List<int> markers = package.TimelineStructure.Markers;

			if (startBeat < 0 && markers != null && markers.Count > 1)
			{
				// seconds per beat ≈ markers[1] / 48000
				double secondsPerBeat = markers[1] / 48000.0;
				cutDurationSeconds = Math.Abs(startBeat) * secondsPerBeat;
				_logger.LogInformation("StartBeat {StartBeat}: trimming {CutSeconds} seconds from start for AMB generation.", startBeat, cutDurationSeconds);
			}

			string tempDir = Path.Combine(Path.GetTempPath(), $"jde_cooked_{Guid.NewGuid()}");
			Directory.CreateDirectory(tempDir);

			try
			{
				// Convert source to WAV first for processing
				string tempWav = Path.Combine(tempDir, "master.wav");
				IConversion toWav = FFmpeg.Conversions.New();
				toWav.AddParameter($"-y -i \"{masterAudioFile}\" -ar 48000 -ac 2");
				toWav.SetOutput(tempWav);
				toWav.SetOverwriteOutput(true);
				await toWav.Start();

				// Generate AMB intro slice if necessary
				if (cutDurationSeconds > 0.001)
				{
					string ambWav = Path.Combine(tempDir, "amb_intro.wav");
					_logger.LogInformation("Creating AMB intro slice ({CutSeconds}s)...", cutDurationSeconds);
					
					IConversion ambConversion = FFmpeg.Conversions.New();
					ambConversion.AddParameter($"-y -i \"{tempWav}\" -t {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -ar 48000 -ac 2");
					ambConversion.SetOutput(ambWav);
					ambConversion.SetOverwriteOutput(true);
					await ambConversion.Start();

					// Convert AMB to RAKI Opus format
					string ambDestPath = io.Combine(audioAmbFolder, $"amb_{mapNameLower}_intro.wav.ckd");
					WriteCookedAudio(ambWav, ambDestPath, markers, io);
					_logger.LogDebug("Cooked AMB audio: amb_{MapName}_intro", mapNameLower);

					// Create trimmed master WAV (cut the intro)
					string trimmedWav = Path.Combine(tempDir, "trimmed.wav");
					IConversion masterConv = FFmpeg.Conversions.New();
					masterConv.AddParameter($"-y -ss {cutDurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{tempWav}\" -ar 48000 -ac 2");
					masterConv.SetOutput(trimmedWav);
					masterConv.SetOverwriteOutput(true);
					await masterConv.Start();

					// Convert trimmed master to RAKI Opus
					string masterDestPath = io.Combine(audioFolder, $"{mapNameLower}.wav.ckd");
					WriteCookedAudio(trimmedWav, masterDestPath, markers, io);
				}
				else
				{
					// No AMB needed, just convert the full audio
					string masterDestPath = io.Combine(audioFolder, $"{mapNameLower}.wav.ckd");
					WriteCookedAudio(tempWav, masterDestPath, markers, io);
				}

				_logger.LogDebug("Cooked audio: {MapName}", mapNameLower);
			}
			finally
			{
				// Cleanup temp directory
				try
				{
					Directory.Delete(tempDir, true);
				}
				catch { /* ignore cleanup errors */ }
			}
		}
	}

	/// <summary>
	/// Writes all cooked metadata files with .ckd extension.
	/// </summary>
	private async Task WriteCookedFilesAsync(
		IntermediateSongPackage package,
		string mapWorldFolder,
		UbiArtPlatform platform,
		UbiArtEngineVersion engineVersion,
		IUbiArtLayout layout,
		IFileSystem io)
	{
		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
		string mapName = package.Metadata.MapName;

		// Write songdesc.tpl.ckd
		_logger.LogInformation("Writing songdesc.tpl.ckd...");
		string songDescJson = BuildCookedSongDescJson(package, mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(mapWorldFolder, "songdesc.tpl.ckd"), songDescJson);

		// Write songdesc.act.ckd
		string songDescActJson = BuildCookedActorJson("JD_SongDescTemplate", $"world/maps/{mapNameLower}/songdesc.tpl");
		await WriteCookedFileAsync(io, io.Combine(mapWorldFolder, "songdesc.act.ckd"), songDescActJson);

		// Write main scene files
		string mainSceneIscXml = BuildCookedMainSceneIscXml(mapNameLower, package.Metadata.MapName);
		await WriteCookedFileAsync(io, io.Combine(mapWorldFolder, $"{mapNameLower}_main_scene.isc.ckd"), mainSceneIscXml);

		// Write audio files
		string audioFolder = io.Combine(mapWorldFolder, "audio");
		string audioIscXml = BuildCookedAudioIscXml(mapNameLower, package.Metadata.MapName);
		await WriteCookedFileAsync(io, io.Combine(audioFolder, $"{mapNameLower}_audio.isc.ckd"), audioIscXml);

		string musicTrackJson = BuildCookedMusicTrackJson(package, mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl.ckd"), musicTrackJson);

		// Write timeline files
		string timelineFolder = io.Combine(mapWorldFolder, "timeline");
		await WriteCookedTimelineFilesAsync(package, timelineFolder, platform, engineVersion, layout, io);

		// Write cinematics files
		string cinematicsFolder = io.Combine(mapWorldFolder, "cinematics");
		string cinematicsIscXml = BuildCookedCinematicsIscXml(mapNameLower, mapName);
		await WriteCookedFileAsync(io, io.Combine(cinematicsFolder, $"{mapNameLower}_cine.isc.ckd"), cinematicsIscXml);

		// Write menuart ISC
		string menuartFolder = io.Combine(mapWorldFolder, "menuart");
		string menuartIscXml = BuildCookedMenuartIscXml(mapNameLower, mapName);
		await WriteCookedFileAsync(io, io.Combine(menuartFolder, $"{mapNameLower}_menuart.isc.ckd"), menuartIscXml);

		// Write graph ISC
		string graphFolder = io.Combine(mapWorldFolder, "graph");
		string graphIscXml = BuildCookedGraphIscXml(mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(graphFolder, $"{mapNameLower}_graph.isc.ckd"), graphIscXml);

		// Write videoscoach ISC files
		string videosCoachFolder = io.Combine(mapWorldFolder, "videoscoach");
		string videoIscXml = BuildCookedVideoIscXml(mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(videosCoachFolder, $"{mapNameLower}_video.isc.ckd"), videoIscXml);

		// Write video map preview ISC
		string videoMapPreviewIscXml = BuildCookedVideoMapPreviewIscXml(mapNameLower, mapName);
		await WriteCookedFileAsync(io, io.Combine(videosCoachFolder, $"{mapNameLower}_video_map_preview.isc.ckd"), videoMapPreviewIscXml);

		// Write video player ACT files (binary format)
		await WriteCookedVideoPlayerActAsync(io, io.Combine(videosCoachFolder, "video_player_main.act.ckd"), mapNameLower, package.Metadata.MapName, false);
		await WriteCookedVideoPlayerActAsync(io, io.Combine(videosCoachFolder, "video_player_map_preview.act.ckd"), mapNameLower, package.Metadata.MapName, true);

		// Write MPD file (video metadata)
		await WriteCookedMpdAsync(io, io.Combine(videosCoachFolder, $"{mapNameLower}.mpd.ckd"));

		// Write SGS file (scene graph settings) - format: 'S' + JSON + null
		string sgsJson = BuildCookedSgsJson();
		await WriteCookedSgsFileAsync(io, io.Combine(mapWorldFolder, $"{mapNameLower}_main_scene.sgs.ckd"), sgsJson);

		// Write audio sequence.tpl.ckd and stape.ckd
		string sequenceTplJson = BuildCookedSequenceTplJson();
		await WriteCookedFileAsync(io, io.Combine(audioFolder, $"{mapNameLower}_sequence.tpl.ckd"), sequenceTplJson);

		string stapeJson = BuildCookedStapeJson(mapName);
		await WriteCookedFileAsync(io, io.Combine(audioFolder, $"{mapNameLower}.stape.ckd"), stapeJson);

		// Write AMB intro TPL if StartBeat is negative (AMB audio was generated)
		double startBeat = package.TimelineStructure.StartBeat;
		if (startBeat < 0)
		{
			string audioAmbFolder = io.Combine(audioFolder, "amb");
			string ambTplJson = BuildCookedAmbTplJson(mapNameLower);
			await WriteCookedFileAsync(io, io.Combine(audioAmbFolder, $"amb_{mapNameLower}_intro.tpl.ckd"), ambTplJson);
		}

		// Write autodance files
		string autodanceFolder = io.Combine(mapWorldFolder, "autodance");
		string autodanceIscXml = BuildCookedAutodanceIscXml(mapNameLower, mapName);
		await WriteCookedFileAsync(io, io.Combine(autodanceFolder, $"{mapNameLower}_autodance.isc.ckd"), autodanceIscXml);

		string autodanceTplJson = BuildCookedAutodanceTplJson(mapName);
		await WriteCookedFileAsync(io, io.Combine(autodanceFolder, $"{mapNameLower}_autodance.tpl.ckd"), autodanceTplJson);

		// Write autodance actor using dynamic map name (length fields must match mapNameLower)
		using (MemoryStream msAct = new())
		using (BinaryWriter bwAct = new(msAct))
		{
			// Header and fixed fields
			bwAct.Write([
				0x00,0x00,0x00,0x01,
				0x00,0x00,0x00,0x00,
				0x3F,0x80,0x00,0x00,
				0x3F,0x80,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x01,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0x00,0x00,0x00,0x00,
				0xFF,0xFF,0xFF,0xFF,
				0x00,0x00,0x00,0x00,
			]);

			// Filename: <mapNameLower>_autodance.tpl (length as single byte preceded by three zero bytes)
			string autodanceFileName = $"{mapNameLower}_autodance.tpl";
			byte[] autodanceFileNameBytes = Encoding.UTF8.GetBytes(autodanceFileName);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)autodanceFileNameBytes.Length);
			bwAct.Write(autodanceFileNameBytes);

			// Path: world/maps/{mapNameLower}/autodance/ (length as single byte preceded by three zero bytes)
			string autodancePath = $"world/maps/{mapNameLower}/autodance/";
			byte[] autodancePathBytes = Encoding.UTF8.GetBytes(autodancePath);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)0x00);
			bwAct.Write((byte)autodancePathBytes.Length);
			bwAct.Write(autodancePathBytes);

			// Tail bytes (hash and padding) - match expected sequence
			bwAct.Write([0xD7, 0x50, 0x31, 0x3C]);
			bwAct.Write(new byte[11]); // padding zeros
			bwAct.Write((byte)0x01);
			bwAct.Write([0x67, 0xB8, 0xBB, 0x77]);

			string autodanceActPath = io.Combine(autodanceFolder, $"{mapNameLower}_autodance.act.ckd");
            await using FileStream fs = new(autodanceActPath, FileMode.Create, FileAccess.Write);
            msAct.Position = 0;
            await msAct.CopyToAsync(fs);
        }

		// Write cinematics mainsequence files
		string mainseqTplJson = BuildCookedMainsequenceTplJson(mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tpl.ckd"), mainseqTplJson);

		string mainseqActJson = BuildCookedActorJson("MasterTape", $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl");
		await WriteCookedFileAsync(io, io.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.act.ckd"), mainseqActJson);

		string mainseqTapeJson = BuildCookedMainsequenceTapeJson(package, mapName);
		await WriteCookedFileAsync(io, io.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tape.ckd"), mainseqTapeJson);

		// Write menuart actors for each texture
		string menuartActorsFolder = io.Combine(menuartFolder, "actors");
		string menuartTexturesFolder = io.Combine(menuartFolder, "textures");
		await WriteCookedMenuartActorsAsync(io, menuartActorsFolder, menuartTexturesFolder, mapNameLower);
	}

	/// <summary>
	/// Writes a cooked file with trailing null byte.
	/// </summary>
	private static async Task WriteCookedFileAsync(IFileSystem io, string filePath, string jsonContent)
	{
		byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonContent);
		byte[] withNullByte = new byte[jsonBytes.Length + 1];
		Array.Copy(jsonBytes, withNullByte, jsonBytes.Length);
		withNullByte[^1] = 0;  // Add null byte at the end

		await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
		await fs.WriteAsync(withNullByte);
	}

	/// <summary>
	/// Builds a cooked SongDesc JSON.
	/// </summary>
	private static string BuildCookedSongDescJson(IntermediateSongPackage package, string mapNameLower)
	{
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
					JDVersion = 2022,
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
                        songcolor_1a = new[] { 1, 1, 1, 1 },
                        songcolor_1b = new[] { 1, 1, 1, 1 },
                        songcolor_2a = new[] { 1, 1, 1, 1 },
                        songcolor_2b = new[] { 1, 1, 1, 1 },
                        lyrics = ParseLyricsColor(package.Metadata.LyricsColor),
						theme = new[] { 1.0, 1, 1, 1 }
					}
				}
			}
		};
		return System.Text.Json.JsonSerializer.Serialize(songDesc, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Converts a hex color string (e.g., "#RRGGBBAA") to ARGB float array [A, R, G, B] in 0-1 range.
	/// </summary>
	private static double[] ParseLyricsColor(string hexColor)
	{
		// Default to white with full opacity if invalid
		if (string.IsNullOrWhiteSpace(hexColor) || !hexColor.StartsWith('#') || hexColor.Length != 9)
		{
			return [1.0, 1.0, 1.0, 1.0];
		}

		try
		{
			// Parse hex string: #RRGGBBAA
			string hex = hexColor[1..];
			int r = int.Parse(hex[..2], NumberStyles.HexNumber);
			int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
			int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
			int a = int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);

			// Convert to 0-1 range and return as ARGB
			return
            [
                a / 255.0,
				r / 255.0,
				g / 255.0,
				b / 255.0
			];
		}
		catch
		{
			// Fall back to white with full opacity
			return [1.0, 1.0, 1.0, 1.0];
		}
	}

	/// <summary>
	/// Builds a cooked Actor JSON for .act.ckd files.
	/// </summary>
	private static string BuildCookedActorJson(string className, string tplPath)
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
			LUA = tplPath,
			COMPONENTS = Array.Empty<object>()
		};
		return System.Text.Json.JsonSerializer.Serialize(actor, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked main scene ISC XML matching UbiArt format.
	/// </summary>
	private static string BuildCookedMainSceneIscXml(string mapNameLower, string mapName)
	{
		// Build the full main scene with embedded subscenes
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""2.000000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<PLATFORM_FILTER>
			<TargetFilterList platform=""WII"">
				<objects VAL=""{mapName}_AUTODANCE"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_AUDIO"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/audio/{mapNameLower}_audio.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedAudioIscXmlEmbedded(mapNameLower, mapName)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_CINE"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_cine.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedCinematicsIscXmlEmbedded(mapNameLower, mapName)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_GRAPH"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/graph/{mapNameLower}_graph.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedGraphIscXmlEmbedded(mapNameLower)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_TML"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedTimelineIscXmlEmbedded(mapNameLower, mapName)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_VIDEO"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/videoscoach/{mapNameLower}_video.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedVideoIscXmlEmbedded(mapNameLower)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName} : Template Artist - Template Title&#10;JDVer = 5, ID = 842776738, Type = 1 (Flags 0x00000000), NbCoach = 2, Difficulty = 2"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-3.531976 -1.485322"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/songdesc.tpl"">
				<COMPONENTS NAME=""JD_SongDescComponent"">
					<JD_SongDescComponent />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_menuart"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/menuart/{mapNameLower}_menuart.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""3"" />
				<SCENE>
					{BuildCookedMenuartIscXmlEmbedded(mapNameLower, mapName)}
				</SCENE>
			</SubSceneActor>
		</ACTORS>
		<ACTORS NAME=""SubSceneActor"">
			<SubSceneActor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_AUTODANCE"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 -0.033823"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/subscene.tpl"" RELATIVEPATH=""world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.isc"" EMBED_SCENE=""1"" IS_SINGLE_PIECE=""0"" ZFORCED=""1"" DIRECT_PICKING=""1"" IGNORE_SAVE=""0"">
				<ENUM NAME=""viewType"" SEL=""2"" />
				<SCENE>
					{BuildCookedAutodanceIscXmlEmbedded(mapNameLower, mapName)}
				</SCENE>
			</SubSceneActor>
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
	}

	/// <summary>
	/// Builds cooked audio ISC XML.
	/// </summary>
	private static string BuildCookedAudioIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	{BuildCookedAudioIscXmlEmbedded(mapNameLower, mapName)}
</root>
";
	}

	/// <summary>
	/// Builds cooked music track JSON.
	/// </summary>
	private static string BuildCookedMusicTrackJson(IntermediateSongPackage package, string mapNameLower)
	{
		string mapName = package.Metadata.MapName;

        // Build signatures array from structure
        Signature[] signatures = [.. package.TimelineStructure.Signatures
			.Select(s => new Signature
			{
				Beats = s.Beats,
				Marker = s.Marker
			})];

        // Build sections array from structure
        Section[] sections = [.. package.TimelineStructure.Sections
			.Select(sec => new Section
			{
				Marker = (float)sec.StartBeat,
				SectionType = (int)sec.SectionType,
				Comment = sec.Comment ?? string.Empty
			})];

        // Build structure with all fade and volume fields
        Structure structure = new()
        {
			StartBeat = package.TimelineStructure.StartBeat,
			EndBeat = package.TimelineStructure.EndBeat,
			VideoStartTime = (float)package.TimelineStructure.VideoStartOffset,
			PreviewEntry = package.TimelineStructure.PreviewEntryBeat,
			PreviewLoopStart = package.TimelineStructure.PreviewLoopStartBeat,
			PreviewLoopEnd = package.TimelineStructure.PreviewLoopEndBeat,
			PreviewDuration = package.TimelineStructure.PrevewDuration,
			Markers = [.. package.TimelineStructure.Markers],
			Signatures = signatures,
			Sections = sections,
			FadeStartBeat = 0,
			FadeEndBeat = 0,
			FadeInDuration = 0,
			FadeOutDuration = 0,
			FadeInType = 0,
			FadeOutType = 0,
			UseFadeStartBeat = false,
			UseFadeEndBeat = false,
			Volume = 0
		};

        MusicTrack musicTrack = new()
        {
			Class = "Actor_Template",
			Wip = 0,
			LowUpdate = 0,
			UpdateLayer = 0,
			Procedural = 0,
			StartPaused = 0,
			ForceIsEnvironment = 0,
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

        JsonSerializerOptions options = new()
        {
			WriteIndented = false,
			PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
		};
		return System.Text.Json.JsonSerializer.Serialize(musicTrack, options);
	}

	/// <summary>
	/// Writes cooked timeline files.
	/// </summary>
	private async Task WriteCookedTimelineFilesAsync(
		IntermediateSongPackage package,
		string timelineFolder,
		UbiArtPlatform platform,
		UbiArtEngineVersion engineVersion,
		IUbiArtLayout layout,
		IFileSystem io)
	{
		string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

		// Write timeline ISC
		string timelineIscXml = BuildCookedTimelineIscXml(mapNameLower, package.Metadata.MapName);
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml.isc.ckd"), timelineIscXml);

		// Write dance tape (dtape.ckd)
		string dtapeJson = BuildCookedDtapeJson(package, mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_dance.dtape.ckd"), dtapeJson);

		// Write dance act
		string danceActJson = BuildCookedActorJson("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl");
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_dance.act.ckd"), danceActJson);

		// Write dance tpl
		string danceTplJson = BuildCookedTapeCaseTplJson(mapNameLower, "dance");
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_dance.tpl.ckd"), danceTplJson);

		// Write karaoke tape (ktape.ckd)
		string ktapeJson = BuildCookedKtapeJson(package, mapNameLower);
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.ktape.ckd"), ktapeJson);

		// Write karaoke act
		string karaokeActJson = BuildCookedActorJson("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl");
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.act.ckd"), karaokeActJson);

		// Write karaoke tpl
		string karaokeTplJson = BuildCookedTapeCaseTplJson(mapNameLower, "karaoke");
		await WriteCookedFileAsync(io, io.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.tpl.ckd"), karaokeTplJson);
	}

	/// <summary>
	/// Builds cooked timeline ISC XML.
	/// </summary>
	private static string BuildCookedTimelineIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000001"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_dance"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-1.157740 0.006158"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl"">
				<COMPONENTS NAME=""TapeCase_Component"">
					<TapeCase_Component />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000001"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_karaoke"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-1.157740 0.006158"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl"">
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
	}

	/// <summary>
	/// Builds cooked dance tape JSON (dtape).
	/// </summary>
	private static string BuildCookedDtapeJson(IntermediateSongPackage package, string mapNameLower)
	{
        List<object> clips = [];

		// Add MotionClips
		foreach (MoveTimeline timeline in package.CoachTimelines)
		{
			foreach (MoveClip clip in timeline.Clips)
			{
				package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move);
				if (move == null)
					continue;

				// Parse color from hex to RGBA floats
				double[] color = ParseColorToRgba(move.Color);

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
					Color = color,
					MotionPlatformSpecifics = new
					{
						X360 = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 },
						ORBIS = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = -0.2, HighThreshold = 0.6 },
						DURANGO = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 }
					}
				});
			}
		}

		// Add PictogramClips
		foreach (PictogramClip pictoClip in package.Pictograms.Clips)
		{
			clips.Add(new
			{
				__class = "PictogramClip",
				pictoClip.Id,
				TrackId = PictoTrackId,
				IsActive = 1,
				pictoClip.StartTime,
				pictoClip.Duration,
				PictoPath = $"world/maps/{mapNameLower}/timeline/pictos/{pictoClip.PictogramId}.png",
				CoachCount = 4294967295u
			});
		}

		// Add GoldEffectClips
		foreach (GoldEffectClip goldClip in package.GoldEffects.Clips)
		{
			clips.Add(new
			{
				__class = "GoldEffectClip",
				goldClip.Id,
				TrackId = GoldEffectTrackId,
				IsActive = 1,
				goldClip.StartTime,
				goldClip.Duration,
				goldClip.EffectType
			});
		}

        // Sort clips by StartTime
        List<object> sortedClips = [.. clips.OrderBy(c => ((dynamic)c).StartTime)];

		var dtape = new
		{
			__class = "Tape",
			Clips = sortedClips,
			TapeClock = 0,
			TapeBarCount = 1,
			FreeResourcesAfterPlay = 0,
			MapName = mapNameLower,
			SoundwichEvent = ""
		};

		return System.Text.Json.JsonSerializer.Serialize(dtape, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Parses a hex color string to RGBA float array.
	/// </summary>
	private static double[] ParseColorToRgba(string hexColor)
	{
		if (string.IsNullOrEmpty(hexColor) || hexColor.Length < 7)
			return [1.0, 0.5, 0.5, 0.5];

		try
		{
			string hex = hexColor.TrimStart('#');
			int r = Convert.ToInt32(hex[..2], 16);
			int g = Convert.ToInt32(hex.Substring(2, 2), 16);
			int b = Convert.ToInt32(hex.Substring(4, 2), 16);
			return [1.0, r / 255.0, g / 255.0, b / 255.0];
		}
		catch
		{
			return [1.0, 0.5, 0.5, 0.5];
		}
	}

	/// <summary>
	/// Builds cooked karaoke tape JSON (ktape) matching UbiArt format.
	/// </summary>
	private static string BuildCookedKtapeJson(IntermediateSongPackage package, string mapNameLower)
	{
		string mapNameCapitalized = package.Metadata.MapName;

        List<object> clips = [];

		foreach (KaraokeClip lyric in package.Lyrics.Clips)
		{
			clips.Add(new
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
			});
		}

		var ktape = new
		{
			__class = "Tape",
			Clips = clips.OrderBy(c => ((dynamic)c).StartTime).ToList(),
			TapeClock = 0,
			TapeBarCount = 1,
			FreeResourcesAfterPlay = 0,
			MapName = mapNameCapitalized,
			SoundwichEvent = ""
		};

		return System.Text.Json.JsonSerializer.Serialize(ktape, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked TapeCase TPL JSON matching UbiArt format.
	/// </summary>
	private static string BuildCookedTapeCaseTplJson(string mapNameLower, string tapeType)
	{
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
		return System.Text.Json.JsonSerializer.Serialize(tpl, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked cinematics ISC XML.
	/// </summary>
	private static string BuildCookedCinematicsIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_MainSequence"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl"">
				<COMPONENTS NAME=""MasterTape"">
					<MasterTape />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
	}

	/// <summary>
	/// Builds cooked menuart ISC XML.
	/// </summary>
	private static string BuildCookedMenuartIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	{BuildCookedMenuartIscXmlEmbedded(mapNameLower, mapName)}
</root>";
	}

	private static string BuildCookedMenuartIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""1"" isPopup=""0"">
		<PLATFORM_FILTER>
			<TargetFilterList platform=""WII"">
				<objects VAL=""{mapName}_banner_bkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<PLATFORM_FILTER>
			<TargetFilterList platform=""PS3"">
				<objects VAL=""{mapName}_banner_bkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<PLATFORM_FILTER>
			<TargetFilterList platform=""X360"">
				<objects VAL=""{mapName}_banner_bkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<PLATFORM_FILTER>
			<TargetFilterList platform=""WIIU"">
				<objects VAL=""{mapName}_cover_generic"" />
				<objects VAL=""{mapName}_cover_albumbkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<PLATFORM_FILTER>
			<TargetFilterList platform=""ORBIS"">
				<objects VAL=""{mapName}_cover_generic"" />
				<objects VAL=""{mapName}_cover_albumbkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<PLATFORM_FILTER>
			<TargetFilterList platform=""DURANGO"">
				<objects VAL=""{mapName}_cover_generic"" />
				<objects VAL=""{mapName}_cover_albumbkg"" />
			</TargetFilterList>
		</PLATFORM_FILTER>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.300000 0.300000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_generic"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""266.087555 197.629959"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_generic.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.300000 0.300000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_online_Kids"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-150.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_online_kids.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.300000 0.300000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_online"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-150.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_online.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.300000 0.300000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_albumcoach"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""738.106323 359.612030"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""6"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_albumcoach.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""6"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.300000 0.300000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_cover_albumbkg"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""1067.972168 201.986328"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_albumbkg.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""256.000000 128.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_banner_bkg"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""1487.410156 -32.732918"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""1"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_banner_bkg.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""0.290211 0.290211"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_coach_1"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""212.784500 663.680176"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""6"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_coach_1.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""6"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""256.000000 128.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_map_bkg"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""1487.410034 350.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl"">
				<COMPONENTS NAME=""MaterialGraphicComponent"">
					<MaterialGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""1"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"">
						<PrimitiveParameters>
							<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
								<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
							</GFXPrimitiveParam>
						</PrimitiveParameters>
						<ENUM NAME=""anchor"" SEL=""1"" />
						<material>
							<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/multitexture_1layer.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
								<textureSet>
									<GFXMaterialTexturePathSet diffuse=""world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_map_bkg.tga"" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
								</textureSet>
								<materialParams>
									<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
								</materialParams>
								<outlinedMaskParams>
									<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
								</outlinedMaskParams>
							</GFXMaterialSerializable>
						</material>
						<ENUM NAME=""oldAnchor"" SEL=""1"" />
					</MaterialGraphicComponent>
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>";
	}

	private static string BuildCookedAudioIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""MusicTrack"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""1.125962 -0.418641"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/audio/{mapNameLower}_musictrack.tpl"">
								<COMPONENTS NAME=""MusicTrackComponent"">
									<MusicTrackComponent />
								</COMPONENTS>
							</Actor>
						</ACTORS>
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""0.000001"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_sequence"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-0.006158 -0.006158"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/audio/{mapNameLower}_sequence.tpl"">
								<COMPONENTS NAME=""TapeCase_Component"">
									<TapeCase_Component />
								</COMPONENTS>
							</Actor>
						</ACTORS>
						<sceneConfigs>
							<SceneConfigs activeSceneConfig=""0"" />
						</sceneConfigs>
					</Scene>";
	}

	private static string BuildCookedCinematicsIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_MainSequence"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl"">
								<COMPONENTS NAME=""MasterTape"">
									<MasterTape />
								</COMPONENTS>
							</Actor>
						</ACTORS>
						<sceneConfigs>
							<SceneConfigs activeSceneConfig=""0"" />
						</sceneConfigs>
					</Scene>";
	}

	private static string BuildCookedGraphIscXmlEmbedded(string mapNameLower)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""10.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""Camera_JD_Dummy"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_emptyactor.tpl"" />
						</ACTORS>
						<sceneConfigs>
							<SceneConfigs activeSceneConfig=""0"" />
						</sceneConfigs>
					</Scene>";
	}

	private static string BuildCookedTimelineIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""0.000001"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_dance"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-1.157740 0.006158"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl"">
								<COMPONENTS NAME=""TapeCase_Component"">
									<TapeCase_Component />
								</COMPONENTS>
							</Actor>
						</ACTORS>
						<ACTORS NAME=""Actor"">
							<Actor RELATIVEZ=""0.000001"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_tml_karaoke"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-1.157740 0.006158"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl"">
								<COMPONENTS NAME=""TapeCase_Component"">
									<TapeCase_Component />
								</COMPONENTS>
							</Actor>
						</ACTORS>
						<sceneConfigs>
							<SceneConfigs activeSceneConfig=""0"" />
						</sceneConfigs>
					</Scene>";
	}

	private static string BuildCookedVideoIscXmlEmbedded(string mapNameLower)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
					<ACTORS NAME=""Actor"">
						<Actor RELATIVEZ=""-1.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""VideoScreen"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 -4.500000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/_common/videoscreen/video_player_main.tpl"">
							<COMPONENTS NAME=""PleoComponent"">
								<PleoComponent video=""world/maps/{mapNameLower}/videoscoach/{mapNameLower}.webm"" dashMPD=""world/maps/{mapNameLower}/videoscoach/{mapNameLower}.mpd"" channelID="""" />
							</COMPONENTS>
						</Actor>
					</ACTORS>
					<ACTORS NAME=""Actor"">
						<Actor RELATIVEZ=""0.000000"" SCALE=""3.941238 2.220000"" xFLIPPED=""0"" USERFRIENDLY=""VideoOutput"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/_common/videoscreen/video_output_main.tpl"">
							<COMPONENTS NAME=""PleoTextureGraphicComponent"">
								<PleoTextureGraphicComponent colorComputerTagId=""0"" renderInTarget=""0"" disableLight=""0"" disableShadow=""4294967295"" AtlasIndex=""0"" customAnchor=""0.000000 0.000000"" SinusAmplitude=""0.000000 0.000000 0.000000"" SinusSpeed=""1.000000"" AngleX=""0.000000"" AngleY=""0.000000"" channelID="""">
									<PrimitiveParameters>
										<GFXPrimitiveParam colorFactor=""1.000000 1.000000 1.000000 1.000000"">
											<ENUM NAME=""gfxOccludeInfo"" SEL=""0"" />
										</GFXPrimitiveParam>
									</PrimitiveParameters>
									<ENUM NAME=""anchor"" SEL=""1"" />
									<material>
										<GFXMaterialSerializable ATL_Channel=""0"" ATL_Path="""" shaderPath=""world/_common/matshader/pleofullscreen.msh"" alphaTest=""4294967295"" alphaRef=""4294967295"">
											<textureSet>
												<GFXMaterialTexturePathSet diffuse="""" back_light="""" normal="""" separateAlpha="""" diffuse_2="""" back_light_2="""" anim_impostor="""" diffuse_3="""" diffuse_4="""" />
											</textureSet>
											<materialParams>
												<GFXMaterialSerializableParam Reflector_factor=""0.000000"" />
											</materialParams>
											<outlinedMaskParams>
												<OutlinedMaskMaterialParams maskColor=""0.000000 0.000000 0.000000 0.000000"" outlineColor=""0.000000 0.000000 0.000000 0.000000"" thickness=""1.000000"" />
											</outlinedMaskParams>
										</GFXMaterialSerializable>
									</material>
									<ENUM NAME=""oldAnchor"" SEL=""1"" />
								</PleoTextureGraphicComponent>
							</COMPONENTS>
						</Actor>
					</ACTORS>
					<sceneConfigs>
						<SceneConfigs activeSceneConfig=""0"" />
					</sceneConfigs>
				</Scene>";
	}

	private static string BuildCookedAutodanceIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
					<ACTORS NAME=""Actor"">
						<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_autodance"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""-0.006150 -0.003075"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.tpl"">
							<COMPONENTS NAME=""JD_AutodanceComponent"">
								<JD_AutodanceComponent />
							</COMPONENTS>
						</Actor>
					</ACTORS>
					<sceneConfigs>
						<SceneConfigs activeSceneConfig=""0"" />
					</sceneConfigs>
				</Scene>";
	}

	/// <summary>
	/// Builds cooked graph ISC XML.
	/// </summary>
	private static string BuildCookedGraphIscXml(string mapNameLower)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""10.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""Camera_JD_Dummy"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""enginedata/actortemplates/tpl_emptyactor.tpl"" />
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>
</root>
";
	}

	/// <summary>
	/// Builds cooked video ISC XML.
	/// </summary>
	private static string BuildCookedVideoIscXml(string mapNameLower)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	{BuildCookedVideoIscXmlEmbedded(mapNameLower)}
</root>
";
	}

	/// <summary>
	/// Builds cooked video map preview ISC XML.
	/// </summary>
	private static string BuildCookedVideoMapPreviewIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	{BuildCookedVideoMapPreviewIscXmlEmbedded(mapNameLower, mapName)}
</root>
";
	}

	private static string BuildCookedVideoMapPreviewIscXmlEmbedded(string mapNameLower, string mapName)
	{
		return $@"<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""-1.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""VideoScreen"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 -4.500000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/_common/videoscreen/video_player_map_preview.tpl"">
				<COMPONENTS NAME=""PleoComponent"">
					<PleoComponent video=""world/maps/{mapNameLower}/videoscoach/{mapNameLower}.webm"" dashMPD=""world/maps/{mapNameLower}/videoscoach/{mapNameLower}.mpd"" channelID=""{mapName}"" />
				</COMPONENTS>
			</Actor>
		</ACTORS>
		<sceneConfigs>
			<SceneConfigs activeSceneConfig=""0"" />
		</sceneConfigs>
	</Scene>";
	}

	/// <summary>
	/// Builds cooked SGS JSON (scene graph settings).
	/// </summary>
	private static string BuildCookedSgsJson()
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
		return System.Text.Json.JsonSerializer.Serialize(sgs, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Writes a cooked SGS file (format: 'S' + JSON + null).
	/// </summary>
	private static async Task WriteCookedSgsFileAsync(IFileSystem io, string filePath, string jsonContent)
	{
		byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonContent);
		byte[] content = new byte[1 + jsonBytes.Length + 1];
		content[0] = (byte)'S'; // SGS prefix
		Array.Copy(jsonBytes, 0, content, 1, jsonBytes.Length);
		content[^1] = 0; // Null terminator

		await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
		await fs.WriteAsync(content);
	}

	/// <summary>
	/// Builds cooked sequence TPL JSON (TapeCase_Template for audio sequence).
	/// </summary>
	private static string BuildCookedSequenceTplJson()
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
				new { __class = "TapeCase_Template" }
			}
		};
		return System.Text.Json.JsonSerializer.Serialize(tpl, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked STAPE JSON (sound tape).
	/// </summary>
	private static string BuildCookedStapeJson(string mapName)
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
		return System.Text.Json.JsonSerializer.Serialize(stape, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked AMB (ambient) intro TPL JSON (SoundComponent template).
	/// </summary>
	private static string BuildCookedAmbTplJson(string mapNameLower)
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
							files = new object[]
							{
								$"world/maps/{mapNameLower}/audio/amb/amb_{mapNameLower}_intro.wav"
							}
						}
					}
				}
			}
		};
		return System.Text.Json.JsonSerializer.Serialize(tpl, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked autodance ISC XML.
	/// </summary>
	private static string BuildCookedAutodanceIscXml(string mapNameLower, string mapName)
	{
		return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<root>
	<Scene ENGINE_VERSION=""326704"" GRIDUNIT=""0.500000"" DEPTH_SEPARATOR=""0"" NEAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" FAR_SEPARATOR=""1.000000 0.000000 0.000000 0.000000, 0.000000 1.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000"" viewFamily=""0"" isPopup=""0"">
		<ACTORS NAME=""Actor"">
			<Actor RELATIVEZ=""0.000000"" SCALE=""1.000000 1.000000"" xFLIPPED=""0"" USERFRIENDLY=""{mapName}_Autodance"" MARKER="""" DEFAULTENABLE=""1"" POS2D=""0.000000 0.000000"" ANGLE=""0.000000"" INSTANCEDATAFILE="""" LUA=""world/maps/{mapNameLower}/autodance/{mapNameLower}_autodance.tpl"">
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
	}

	/// <summary>
	/// Builds cooked autodance TPL JSON.
	/// </summary>
	private static string BuildCookedAutodanceTplJson(string mapName)
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
					song = mapName,
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
		return System.Text.Json.JsonSerializer.Serialize(tpl, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked mainsequence TPL JSON.
	/// </summary>
	private static string BuildCookedMainsequenceTplJson(string mapNameLower)
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
							//path = $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tape"
		return System.Text.Json.JsonSerializer.Serialize(tpl, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Builds cooked mainsequence tape JSON.
	/// </summary>
	private static string BuildCookedMainsequenceTapeJson(IntermediateSongPackage package, string mapName)
	{
		// Build the Clips array with HideUserInterfaceClip and SoundSetClip
		List<object> clips = [];
		
		// Add HideUserInterfaceClip entries from the package
		long clipIdCounter = 12345; // Start with a reasonable ID
		if (package.HideUserInterface?.Clips != null && package.HideUserInterface.Clips.Count > 0)
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
		
		// Add SoundSetClip if AMB audio exists
		// Check if AMB intro file was generated based on StartBeat
		bool hasAmbIntro = false;
		if (package.TimelineStructure.StartBeat < 0 && 
			package.TimelineStructure.Markers != null && 
			package.TimelineStructure.Markers.Count > 1)
		{
			hasAmbIntro = true;
		}
		
		if (hasAmbIntro)
		{
			long ambClipId = 67890;
			long ambTrackId = 2222;
			int ambDuration = 1200; // Default duration; adjust if needed
			
			clips.Add(new
			{
				__class = "SoundSetClip",
				Id = ambClipId,
				TrackId = ambTrackId,
				IsActive = 1,
				StartTime = package.TimelineStructure.StartBeat * 24,
				Duration = ambDuration,
				SoundSetPath = $"world/maps/{mapName}/audio/amb/amb_{mapName}_intro.tpl",
				SoundChannel = 0,
				StartOffset = 0,
				StopsOnEnd = 0,
				AccountedForDuration = 0
			});
		}
		
		object tape = new
		{
			__class = "Tape",
			Clips = clips,
			TapeClock = 0,
			TapeBarCount = 1,
			FreeResourcesAfterPlay = 0,
			MapName = mapName,
			SoundwichEvent = ""
		};
		return System.Text.Json.JsonSerializer.Serialize(tape, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Writes a cooked MPD file (video metadata).
	/// MPD is a small binary file with video playback parameters.
	/// </summary>
	private static async Task WriteCookedMpdAsync(IFileSystem io, string filePath)
	{
		// MPD format: 17 bytes of video metadata
		// Based on reference: 00 00 00 01 00 42 4B AE 14 3F 80 00 00 00 00 00 00
		byte[] mpd =
		[
			0x00, 0x00, 0x00, 0x01,  // Version/magic
			0x00, 0x42, 0x4B, 0xAE,  // Video parameters (could be related to bitrate/size)
			0x14, 0x3F, 0x80, 0x00,  // Float value (1.0 in BE)
			0x00, 0x00, 0x00, 0x00,  // Padding
			0x00                     // Terminator
		];

		await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
		await fs.WriteAsync(mpd);
	}

	/// <summary>
	/// Writes a cooked video player ACT file (binary actor format).
	/// </summary>
	private static async Task WriteCookedVideoPlayerActAsync(IFileSystem io, string filePath, string mapNameLower, string mapName, bool isMapPreview)
	{
		string actorName = isMapPreview ? "video_player_map_preview" : "video_player_main";
		string videoPath = $"world/maps/{mapNameLower}/videoscoach/";
		string webmFile = $"{mapNameLower}.webm";
		string mpdFile = $"{mapNameLower}.mpd";
		string tplPath = "world/_common/videoscreen/";
		string tplName = isMapPreview ? "video_player_map_preview.tpl" : "video_player_main.tpl";

		using MemoryStream ms = new();
		using BinaryWriter writer = new(ms);

		// Actor header (based on reference format)
		// 0x00-0x03: Magic/version
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x01);

		// 0x04-0x0F: Transform data (identity)
		writer.Write((uint)0x00000000);  // Flags
		WriteBigEndianFloat(writer, 1.0f);              // ScaleX (BE)
		WriteBigEndianFloat(writer, 1.0f);              // ScaleY (BE)

		// 0x10-0x1F: More transform
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		WriteBigEndian32(writer, 0x00000001);

		// 0x20-0x2F: Padding
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// 0x30-0x3F: More flags
		writer.Write((uint)0x00000000);
		writer.Write(0xFFFFFFFF);  // -1 marker
		writer.Write((uint)0x00000000);

		// Write TPL name length and string
		byte[] tplNameBytes = Encoding.UTF8.GetBytes(tplName);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)tplNameBytes.Length);
		writer.Write(tplNameBytes);

		// Write TPL path length and string  
		byte[] tplPathBytes = Encoding.UTF8.GetBytes(tplPath);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)tplPathBytes.Length);
		writer.Write(tplPathBytes);

		// Write hash/ID (varies by video player type)
		if (isMapPreview)
		{
			writer.Write((byte)0xD3);
			writer.Write((byte)0x94);
			writer.Write((byte)0x54);
			writer.Write((byte)0x28);
		}
		else
		{
			writer.Write((byte)0xF5);
			writer.Write((byte)0xD5);
			writer.Write((byte)0xE8);
			writer.Write((byte)0xF2);
		}

		// Padding
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Component data marker
		writer.Write((byte)0x00);
		writer.Write((byte)0x01);

		// Additional hash
		writer.Write((byte)0x12);
		writer.Write((byte)0x63);
		writer.Write((byte)0xDA);
		writer.Write((byte)0xD9);

		writer.Write((uint)0x00000000);

		// Write webm filename length and string
		byte[] webmBytes = Encoding.UTF8.GetBytes(webmFile);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)webmBytes.Length);
		writer.Write(webmBytes);
		writer.Write((byte)0x00);

		// Write video path length and string
		byte[] videoPathBytes = Encoding.UTF8.GetBytes(videoPath);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)videoPathBytes.Length);
		writer.Write(videoPathBytes);

		// Write hash
		writer.Write((byte)0x56);
		writer.Write((byte)0x0F);
		writer.Write((byte)0xF1);
		writer.Write((byte)0x7A);

		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Write MPD filename length and string
		byte[] mpdBytes = Encoding.UTF8.GetBytes(mpdFile);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)mpdBytes.Length);
		writer.Write(mpdBytes);
		writer.Write((byte)0x00);

		// Write video path again for MPD
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)videoPathBytes.Length);
		writer.Write(videoPathBytes);

		// Final hash and padding
		writer.Write((byte)0x26);
		writer.Write((byte)0x8C);
		writer.Write((byte)0x76);
		writer.Write((byte)0x14);

		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// For map preview, write channelID (mapName)
		if (isMapPreview)
		{
			byte[] channelBytes = Encoding.UTF8.GetBytes(mapName);
			writer.Write((byte)0x00);
			writer.Write((byte)0x00);
			writer.Write((byte)0x00);
			writer.Write((byte)channelBytes.Length);
			writer.Write(channelBytes);
		}

		await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
		ms.Position = 0;
		await ms.CopyToAsync(fs);
	}

	/// <summary>
	/// Writes cooked menuart actor files for each texture.
	/// Each texture needs a corresponding .act.ckd file with binary actor data.
	/// </summary>
	private static async Task WriteCookedMenuartActorsAsync(IFileSystem io, string actorsFolder, string texturesFolder, string mapNameLower)
	{
		// Get all texture files that exist in the textures folder
		if (!io.DirectoryExists(texturesFolder))
			return;

		foreach (string textureFile in io.GetFiles(texturesFolder, "*.tga.ckd"))
		{
			string textureName = Path.GetFileName(textureFile).Replace(".tga.ckd", "");
			string actorPath = io.Combine(actorsFolder, $"{textureName}.act.ckd");
			
			await WriteCookedMenuartActorAsync(io, actorPath, textureName, mapNameLower);
		}
	}

	/// <summary>
	/// Writes a single cooked menuart actor file (binary format).
	/// </summary>
	private static async Task WriteCookedMenuartActorAsync(IFileSystem io, string filePath, string textureName, string mapNameLower)
	{
		string texturePath = $"world/maps/{mapNameLower}/menuart/textures/";
		string textureFile = $"{textureName}.tga";
		string tplName = "tpl_materialgraphiccomponent2d.tpl";
		string tplPath = "enginedata/actortemplates/";

		using MemoryStream ms = new();
		using BinaryWriter writer = new(ms);

		// Actor header
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x01);

		// Transform (identity)
		writer.Write((uint)0x00000000);
		WriteBigEndianFloat(writer, 1.0f);  // ScaleX
		WriteBigEndianFloat(writer, 1.0f);  // ScaleY

		// More transform/flags
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		WriteBigEndian32(writer, 0x00000001);

		// Padding
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Flags
		writer.Write((uint)0x00000000);
		writer.Write(0xFFFFFFFF);
		writer.Write((uint)0x00000000);

		// TPL name
		byte[] tplNameBytes = Encoding.UTF8.GetBytes(tplName);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)tplNameBytes.Length);
		writer.Write(tplNameBytes);
		//writer.Write((byte)0x00);

		// TPL path
		byte[] tplPathBytes = Encoding.UTF8.GetBytes(tplPath);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)tplPathBytes.Length);
		writer.Write(tplPathBytes);

		// Hash (varies per actor type)
		writer.Write((byte)0xB4);
		writer.Write((byte)0xA8);
		writer.Write((byte)0x17);
		writer.Write((byte)0xA8);

		// More padding and flags
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		WriteBigEndian32(writer, 0x00000001);

		// Component hash
		writer.Write((byte)0x72);
		writer.Write((byte)0xB6);
		writer.Write((byte)0x1F);
		writer.Write((byte)0xC5);

		// Color (white, RGBA floats)
		WriteBigEndianFloat(writer, 1.0f);
		WriteBigEndianFloat(writer, 1.0f);
		WriteBigEndianFloat(writer, 1.0f);
		WriteBigEndianFloat(writer, 1.0f);

		// More padding
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		writer.Write(0xFFFFFFFF);
		writer.Write((uint)0x00000000);

		// Component type marker
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		
		if (textureName.EndsWith("_map_bkg"))
		{
			writer.Write((byte)0x01);
		}
		else
		{
			writer.Write((byte)0x06);
		}

		// More padding
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Texture filename
		byte[] textureFileBytes = Encoding.UTF8.GetBytes(textureFile);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)textureFileBytes.Length);
		writer.Write(textureFileBytes);

		// Texture path
		byte[] texturePathBytes = Encoding.UTF8.GetBytes(texturePath);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)texturePathBytes.Length);
		writer.Write(texturePathBytes);

		// Texture hash (varies by texture name)
		if (textureName.EndsWith("_map_bkg"))
		{
			writer.Write((byte)0x75);
			writer.Write((byte)0xB8);
			writer.Write((byte)0xD3);
			writer.Write((byte)0x38);
		}
		else
		{
			// Default hash for coach textures
			writer.Write((byte)0xCA);
			writer.Write((byte)0x88);
			writer.Write((byte)0x8F);
			writer.Write((byte)0xC5);
		}

		// Padding after texture hash
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Write 8 empty texture slots (padding)
		for (int i = 0; i < 8; i++)
		{
			writer.Write((uint)0x00000000);
			writer.Write(0xFFFFFFFF);
			writer.Write((uint)0x00000000);
			writer.Write((uint)0x00000000);
		}

		// Additional padding before shader
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write(0xFFFFFFFF);
		writer.Write((uint)0x00000000);

		// Shader filename
		string shaderFile = "multitexture_1layer.msh";
		byte[] shaderFileBytes = Encoding.UTF8.GetBytes(shaderFile);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)shaderFileBytes.Length);
		writer.Write(shaderFileBytes);

		// Shader path
		string shaderPath = "world/_common/matshader/";
		byte[] shaderPathBytes = Encoding.UTF8.GetBytes(shaderPath);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)shaderPathBytes.Length);
		writer.Write(shaderPathBytes);

		// Shader hash (0xD7E7D9C7)
		writer.Write((byte)0xD7);
		writer.Write((byte)0xE7);
		writer.Write((byte)0xD9);
		writer.Write((byte)0xC7);

		// Padding after shader hash
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);

		// Final float 1.0 (opacity/multiplier)
		WriteBigEndianFloat(writer, 1.0f);

		// Final padding/terminator section
		writer.Write(0xFFFFFFFF);
		writer.Write(0xFFFFFFFF);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		WriteBigEndianFloat(writer, 1.0f);
		writer.Write((uint)0x00000000);
		writer.Write((uint)0x00000000);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x01);

		await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
		ms.Position = 0;
		await ms.CopyToAsync(fs);
	}

	#region Cooked Asset Conversion (Textures and Audio)

	/// <summary>
	/// Writes a cooked texture file with TEX wrapper header and XTX data for Nintendo Switch.
	/// </summary>
	/// <param name="image">Source image to convert.</param>
	/// <param name="destPath">Destination path for the .ckd file.</param>
	/// <param name="io">File system abstraction.</param>
	private static void WriteCookedTexture(Image<Bgra32> image, string destPath, IFileSystem io)
	{
		io.CreateDirectory(Path.GetDirectoryName(destPath)!);

		using MemoryStream xtxStream = new();
		XTX.ConvertToFile(image, XTX.XTXImageFormat.DXT5, xtxStream);
		byte[] xtxData = xtxStream.ToArray();

		using FileStream fs = File.Create(destPath);
		using BinaryWriter writer = new(fs);

		// Write TEX wrapper header (44 bytes, big-endian values)
		WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)xtxData.Length);

		// Write XTX data (XTX format already includes proper padding/alignment)
		writer.Write(xtxData);
		// Note: No explicit null terminator - XTX data ends with aligned padding
	}

	/// <summary>
	/// Writes the 44-byte TEX wrapper header for Nintendo Switch cooked textures.
	/// </summary>
	private static void WriteTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint xtxDataSize)
	{
		// Calculate texture size in blocks (for alignment info)
		uint widthBlocks = (uint)((width + 3) / 4);
		uint heightBlocks = (uint)((height + 3) / 4);
		uint textureSizeField = widthBlocks * heightBlocks * 16; // Approximate for RGBA8

		// 0x00: Magic (BE: 0x00000009)
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x00);
		writer.Write((byte)0x09);

		// 0x04: "TEX\0"
		writer.Write((byte)'T');
		writer.Write((byte)'E');
		writer.Write((byte)'X');
		writer.Write((byte)0x00);

		// 0x08: XTX data offset (BE: 0x2C = 44)
		WriteBigEndian32(writer, 0x0000002C);

		// 0x0C: Width/format info (LE format)
		// Upper 16 bits: texture width in pixels, lower 16 bits: 0x0080 flag
		uint widthInfo = ((uint)width << 8) | 0x0080;
		writer.Write(widthInfo);

		// 0x10: Width (LE) and Height (LE)
		writer.Write(width);
		writer.Write(height);

		// 0x14: Format info (LE - matches 0x0C structure)
		// 0x00012000 for RGBA8 format with 8KB alignment
		writer.Write(0x00012000);

		// 0x18: Size/format field (LE)
		uint sizeInfo = ((uint)width << 8) | 0x0080;
		writer.Write(sizeInfo);

		// 0x1C: Zero padding
		writer.Write((uint)0);

		// 0x20: Platform marker and size info (LE)
		// Low byte = size field, then "NN" marker bytes
		uint nnMarker = 0x4E4E0004; // Contains "NN" marker with size indicator
		writer.Write(nnMarker);

		// 0x24: Hash/CRC field (LE)
		// CRC based on texture dimensions and fixed seed
		uint crc = (uint)(((width * height) ^ 0xA3E908) & 0xFFFFFFFF);
		writer.Write(crc);

		// 0x28: Zero padding
		writer.Write((uint)0);
	}

	/// <summary>
	/// Writes a 32-bit value in big-endian format.
	/// </summary>
	private static void WriteBigEndian32(BinaryWriter writer, uint value)
	{
		writer.Write((byte)((value >> 24) & 0xFF));
		writer.Write((byte)((value >> 16) & 0xFF));
		writer.Write((byte)((value >> 8) & 0xFF));
		writer.Write((byte)(value & 0xFF));
	}

	/// <summary>
	/// Writes a float value in big-endian format.
	/// </summary>
	private static void WriteBigEndianFloat(BinaryWriter writer, float value)
	{
		byte[] bytes = BitConverter.GetBytes(value);
		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);
		writer.Write(bytes);
	}

	/// <summary>
	/// Converts audio to RAKI Nintendo Opus format for cooked export.
	/// Supports WAV, MP3, OGG, and Opus input formats.
	/// Optionally includes timing metadata (MARK and STRG chunks) for audio synchronization.
	/// Note: Audio files do NOT have null terminators in the cooked format.
	/// </summary>
	/// <param name="sourcePath">Source audio file path.</param>
	/// <param name="destPath">Destination path for the .wav.ckd file.</param>
	/// <param name="markers">Optional markers for timing metadata (sample counts at 48kHz).</param>
	/// <param name="io">File system abstraction.</param>
	private void WriteCookedAudio(string sourcePath, string destPath, IList<int>? markers, IFileSystem io)
	{
		io.CreateDirectory(Path.GetDirectoryName(destPath)!);

		try
		{
			string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
			WaveStream? waveStream = null;
			
			try
			{
				// Choose appropriate reader based on file extension
				waveStream = ext switch
				{
					".opus" or ".ogg" => new OpusWaveStream(sourcePath),
					_ => new AudioFileReader(sourcePath) // WAV, MP3, etc.
				};
				
				using FileStream output = File.Create(destPath);
				
				// Detect if this is an AMB file (ambient/intro) and use PCM encoding
				bool isAmb = Path.GetFileName(destPath).StartsWith("amb_", StringComparison.OrdinalIgnoreCase);
				
				if (isAmb)
				{
					// AMB files use PCM encoding for instant playback without seek/decode overhead
					// The encoder will automatically convert to 16-bit PCM if needed
					RakiAudioEncoder.EncodeToRakiPcm(waveStream, output, platform: "Nx  ", type: "pcm ");
				}
				else
				{
					// Main song uses Opus encoding
					RakiAudioEncoder.EncodeToRakiNxOpus(waveStream, output, markers);
				}
				// Note: No null terminator for audio files
			}
			finally
			{
				waveStream?.Dispose();
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to convert audio {Source} to RAKI format, copying as-is", sourcePath);
			io.Copy(sourcePath, destPath, true);
		}
	}

	#endregion
}
