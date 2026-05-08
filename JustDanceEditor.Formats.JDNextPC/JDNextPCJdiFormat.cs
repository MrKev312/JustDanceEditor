using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;

using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDNextPC;

public sealed class JDNextPCJdiFormat(IMediaProcessor mediaProcessor, ITextureService textureService, ILogger<JDNextPCJdiFormat> logger) : IJdiFormat
{
    private const double SamplesPerSecond = 48000.0;
    private const int TimelineTicksPerBeat = 24;
    private readonly IMediaProcessor _mediaProcessor = mediaProcessor;
    private readonly ITextureService _textureService = textureService;
    private readonly ILogger<JDNextPCJdiFormat> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string DisplayName => "JDNext PC";
    public bool CanImport => true;
    public bool CanExport => true;

    public bool Check(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            return false;

        return File.Exists(Path.Combine(inputPath, "songdesc.json"))
            && File.Exists(Path.Combine(inputPath, "timeline.json"))
            && File.Exists(Path.Combine(inputPath, "musictrack.json"));
    }

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not JDNextPCConversionRequest jdNextRequest)
            throw new ArgumentException("JDNext PC import expects a JDNextPCConversionRequest.", nameof(request));

        ValidateInputFolder(jdNextRequest.InputPath);

        string inputRoot = jdNextRequest.InputPath;
        _logger.LogInformation("Starting JDNext PC -> JDI conversion from '{InputRoot}'", inputRoot);
        _logger.LogDebug("Reading JDNext PC source documents from '{InputRoot}'", inputRoot);

        JDNextSongDescription songDescription = ReadJson<JDNextSongDescription>(Path.Combine(inputRoot, "songdesc.json"));
        JDNextTimelineDocument timelineDocument = ReadJson<JDNextTimelineDocument>(Path.Combine(inputRoot, "timeline.json"));
        JDNextMusicTrackDocument musicTrack = ReadJson<JDNextMusicTrackDocument>(Path.Combine(inputRoot, "musictrack.json"));
        _logger.LogInformation(
            "Loaded JDNext PC metadata for '{Title}' by '{Artist}' with {MoveCount} move clip(s), {LyricCount} lyric clip(s), and {PictoCount} pictogram clip(s)",
            songDescription.Title,
            songDescription.Artist,
            timelineDocument.Moves.Count,
            timelineDocument.Lyrics.Count,
            timelineDocument.Pictos.Count);

        TimelineStructureDocument structure = BuildTimelineStructure(musicTrack, timelineDocument);
        long nextId = 1;

        IntermediateSongPackage package = new()
        {
            Metadata = BuildMetadata(songDescription, inputRoot, musicTrack),
            TimelineStructure = structure,
            Lyrics = BuildLyricsTimeline(structure, timelineDocument, ref nextId),
            Pictograms = BuildPictogramTimeline(structure, timelineDocument, ref nextId),
            GoldEffects = new Timeline<GoldEffectClip>(),
            HideUserInterface = new Timeline<HideUserInterfaceClip>(),
            CoachTimelines = BuildCoachTimelines(structure, timelineDocument, ref nextId),
            FullBodyCoachTimelines = [],
            HandCoachMoves = BuildHandMoveDefinitions(structure, timelineDocument),
            FullBodyCoachMoves = []
        };

        foreach (MoveTimeline moveTimeline in package.CoachTimelines)
        {
            foreach (MoveClip moveClip in moveTimeline.Clips.Where(clip => clip.IsGoldMove))
            {
                int duration = ResolveMoveDurationTicks(package.HandCoachMoves, moveClip.MoveId, moveTimeline, moveClip);
                package.GoldEffects.Clips.Add(new GoldEffectClip
                {
                    Id = nextId++,
                    StartTime = moveClip.StartTime,
                    Duration = duration,
                    IsActive = true,
                    EffectType = 1,
                    TrackId = moveTimeline.TrackId
                });
            }
        }

        string materializedRoot = BuildSuggestedOutputFolder(jdNextRequest.OutputPath, package.Metadata.MapName);
        PrepareMaterializedDirectory(materializedRoot);
        _logger.LogDebug("Prepared JDI materialized directory '{MaterializedRoot}'", materializedRoot);
        _logger.LogInformation("Materializing JDNext PC assets into JDI package at '{OutputRoot}'", materializedRoot);
        await MaterializeAssetsAsync(inputRoot, materializedRoot, timelineDocument, cancellationToken);
        _logger.LogDebug("Writing JDI package metadata to '{MaterializedRoot}'", materializedRoot);
        IntermediatePackageSerializer.WriteToFolder(package, materializedRoot);

        _logger.LogInformation("JDNext PC -> JDI conversion completed for '{MapName}' at '{MaterializedRoot}'", package.Metadata.MapName, materializedRoot);

        return new JdiImportResult(
            package,
            DisplayName,
            materializedRoot,
            MaterializedRootIsTemporary: false,
            SuggestedOutputFolder: materializedRoot);
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not JDNextPCConversionRequest jdNextRequest)
            throw new ArgumentException("JDNext PC export expects a JDNextPCConversionRequest.", nameof(request));

        ArgumentNullException.ThrowIfNull(importResult);
        ArgumentNullException.ThrowIfNull(importResult.Package);

        if (string.IsNullOrWhiteSpace(importResult.MaterializedRoot) || !Directory.Exists(importResult.MaterializedRoot))
            throw new InvalidOperationException("JDNext PC export requires a materialized JDI root.");

        string outputRoot = BuildSuggestedOutputFolder(jdNextRequest.OutputPath, importResult.Package.Metadata.MapName);
        PrepareMaterializedDirectory(outputRoot);
        _logger.LogInformation("Starting JDI -> JDNext PC conversion for '{MapName}' into '{OutputRoot}'", importResult.Package.Metadata.MapName, outputRoot);
        _logger.LogDebug("Building JDNext PC songdesc, musictrack, and timeline documents");

        JDNextSongDescription songDescription = BuildSongDescription(importResult.Package.Metadata);
        JDNextMusicTrackDocument musicTrack = BuildMusicTrack(importResult.Package.TimelineStructure);
        JDNextTimelineDocument timeline = BuildTimeline(importResult.Package);

        WriteJson(Path.Combine(outputRoot, "songdesc.json"), songDescription);
        WriteJson(Path.Combine(outputRoot, "musictrack.json"), musicTrack);
        WriteJson(Path.Combine(outputRoot, "timeline.json"), timeline);

        await ExportAssetsAsync(importResult.Package, importResult.MaterializedRoot, outputRoot, cancellationToken);
        _logger.LogInformation("JDI -> JDNext PC conversion completed for '{MapName}' at '{OutputRoot}'", importResult.Package.Metadata.MapName, outputRoot);
    }

    private static void ValidateInputFolder(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            throw new FileNotFoundException("Input folder not found", inputPath);

        string[] requiredFiles = ["songdesc.json", "timeline.json", "musictrack.json"];
        foreach (string file in requiredFiles)
        {
            string path = Path.Combine(inputPath, file);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing JDNext PC file: {file}", path);
        }
    }

    private static IntermediateMetadata BuildMetadata(JDNextSongDescription songDescription, string inputRoot, JDNextMusicTrackDocument musicTrack)
    {
        string mapName = Path.GetFileName(inputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new IntermediateMetadata
        {
            SongID = Guid.NewGuid(),
            MapName = mapName,
            ParentMapName = mapName,
            Title = songDescription.Title,
            Artist = songDescription.Artist,
            Credits = songDescription.Credits,
            LyricsColor = ToHexColor(songDescription.LyricColor),
            MapLengthSeconds = musicTrack.Beats.Count > 0 ? musicTrack.Beats[^1] : 0,
            OriginalJDVersion = (uint)Math.Max(0, songDescription.JDVersion),
            CoachCount = Math.Max(1, songDescription.NumCoach),
            Difficulty = (uint)Math.Max(0, songDescription.Difficulty),
            SweatDifficulty = (uint)Math.Max(0, songDescription.Difficulty),
            AdditionalMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["jdnextpc.sourceFolder"] = mapName
            }
        };
    }

    private static TimelineStructureDocument BuildTimelineStructure(JDNextMusicTrackDocument musicTrack, JDNextTimelineDocument timelineDocument)
    {
        TimelineStructureDocument structure = new()
        {
            Markers = [.. musicTrack.Beats.Select(seconds => (int)Math.Round(seconds * SamplesPerSecond))],
            StartBeat = -Math.Abs(musicTrack.StartBeat),
            EndBeat = Math.Max(musicTrack.EndBeat, musicTrack.Beats.Count - 1),
            VideoStartOffset = -Math.Abs(musicTrack.VideoStartTime),
            Signatures = [new SignatureSegment { Beats = 4, Marker = 0 }],
            Sections = []
        };

        int previewStartBeat = 0;
        if (timelineDocument.Lyrics.Count > 0)
            previewStartBeat = (int)Math.Round(structure.GetBeatAtSeconds(timelineDocument.Lyrics[0].Time));
        else if (timelineDocument.Moves.Count > 0)
            previewStartBeat = (int)Math.Round(structure.GetBeatAtSeconds(timelineDocument.Moves[0].Time));

        previewStartBeat = Math.Clamp(previewStartBeat, 0, Math.Max(0, structure.EndBeat));
        structure.PreviewEntryBeat = previewStartBeat;
        structure.PreviewLoopStartBeat = previewStartBeat;
        structure.PreviewDuration = 30;
        structure.PreviewLoopEndBeat = Math.Min(
            structure.EndBeat,
            (int)Math.Round(structure.GetBeatAtSeconds(structure.GetSecondsAtBeat(previewStartBeat) + structure.PreviewDuration)));

        return structure;
    }

    private static Timeline<KaraokeClip> BuildLyricsTimeline(TimelineStructureDocument structure, JDNextTimelineDocument timelineDocument, ref long nextId)
    {
        Timeline<KaraokeClip> timeline = new();
        foreach (JDNextLyricClip lyric in timelineDocument.Lyrics.OrderBy(clip => clip.Time))
        {
            timeline.Clips.Add(new KaraokeClip
            {
                Id = nextId++,
                StartTime = SecondsToTimelineTicks(structure, lyric.Time),
                Duration = DurationSecondsToTimelineTicks(structure, lyric.Time, lyric.Duration),
                Lyrics = lyric.Text,
                Pitch = 0,
                IsEndOfLine = lyric.IsLineEnding != 0,
                ContentType = 1
            });
        }

        return timeline;
    }

    private static Timeline<PictogramClip> BuildPictogramTimeline(TimelineStructureDocument structure, JDNextTimelineDocument timelineDocument, ref long nextId)
    {
        Timeline<PictogramClip> timeline = new();
        List<JDNextPictoClip> clips = [.. timelineDocument.Pictos.OrderBy(clip => clip.Time)];

        for (int index = 0; index < clips.Count; index++)
        {
            JDNextPictoClip clip = clips[index];
            double nextTime = index < clips.Count - 1
                ? clips[index + 1].Time
                : clip.Time + 1;

            timeline.Clips.Add(new PictogramClip
            {
                Id = nextId++,
                StartTime = SecondsToTimelineTicks(structure, clip.Time),
                Duration = Math.Max(TimelineTicksPerBeat, DurationSecondsToTimelineTicks(structure, clip.Time, Math.Max(0.01, nextTime - clip.Time))),
                PictogramId = clip.Name,
                CoachCount = -1
            });
        }

        return timeline;
    }

    private static List<MoveTimeline> BuildCoachTimelines(TimelineStructureDocument structure, JDNextTimelineDocument timelineDocument, ref long nextId)
    {
        List<MoveTimeline> timelines = [];

        foreach (IGrouping<int, JDNextMoveClip> group in timelineDocument.Moves
            .OrderBy(clip => clip.CoachID)
            .ThenBy(clip => clip.Time)
            .GroupBy(clip => clip.CoachID))
        {
            MoveTimeline timeline = new()
            {
                CoachId = group.Key,
                TrackId = 4_000_000_000L + group.Key
            };

            foreach (JDNextMoveClip clip in group)
            {
                timeline.Clips.Add(new MoveClip
                {
                    Id = nextId++,
                    StartTime = SecondsToTimelineTicks(structure, clip.Time),
                    MoveId = clip.Name,
                    IsGoldMove = clip.GoldMove != 0
                });
            }

            timelines.Add(timeline);
        }

        return timelines;
    }

    private static Dictionary<string, CoachMoveDefinition> BuildHandMoveDefinitions(TimelineStructureDocument structure, JDNextTimelineDocument timelineDocument)
    {
        return timelineDocument.Moves
            .GroupBy(clip => clip.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new CoachMoveDefinition
                {
                    Color = "#CCCCCC",
                    MoveType = CoachMoveType.HandTracking,
                    Duration = Math.Max(
                        TimelineTicksPerBeat,
                        group.Max(clip => DurationSecondsToTimelineTicks(structure, clip.Time, clip.Duration)))
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static JDNextSongDescription BuildSongDescription(IntermediateMetadata metadata)
    {
        return new JDNextSongDescription
        {
            Title = metadata.Title,
            Artist = metadata.Artist,
            Credits = metadata.Credits,
            JDVersion = (int)metadata.OriginalJDVersion,
            NumCoach = metadata.CoachCount,
            Difficulty = (int)metadata.Difficulty,
            LyricColor = ToNormalizedColor(metadata.LyricsColor)
        };
    }

    private static JDNextMusicTrackDocument BuildMusicTrack(TimelineStructureDocument structure)
    {
        int lastBeatIndex = Math.Max(0, structure.Markers.Count - 1);
        int endBeat = Math.Max(structure.EndBeat, lastBeatIndex);
        int videoEndBeat = Math.Clamp(endBeat - 2, 0, lastBeatIndex);

        return new JDNextMusicTrackDocument
        {
            VideoStartTime = Math.Abs(structure.VideoStartOffset),
            VideoEndTime = structure.Markers.Count == 0 ? 0 : structure.GetSecondsAtBeat(videoEndBeat),
            StartBeat = Math.Abs(structure.StartBeat),
            EndBeat = endBeat,
            Beats = [.. structure.Markers.Select(marker => marker / SamplesPerSecond)]
        };
    }

    private static JDNextTimelineDocument BuildTimeline(IntermediateSongPackage package)
    {
        JDNextTimelineDocument document = new()
        {
            LyricColor = ToNormalizedColor(package.Metadata.LyricsColor),
            Lyrics = [.. package.Lyrics.Clips
                .OrderBy(clip => clip.StartTime)
                .Select(clip => new JDNextLyricClip
                {
                    Time = TimelineTicksToSeconds(package.TimelineStructure, clip.StartTime),
                    Duration = TimelineTicksToDurationSeconds(package.TimelineStructure, clip.StartTime, clip.Duration),
                    Text = clip.Lyrics,
                    IsLineEnding = clip.IsEndOfLine ? 1 : 0
                })],
            Pictos = [.. package.Pictograms.Clips
                .OrderBy(clip => clip.StartTime)
                .Select(clip => new JDNextPictoClip
                {
                    Time = TimelineTicksToSeconds(package.TimelineStructure, clip.StartTime),
                    Name = clip.PictogramId
                })],
            Moves = []
        };

        foreach (MoveTimeline moveTimeline in package.CoachTimelines.OrderBy(timeline => timeline.CoachId))
        {
            foreach (MoveClip moveClip in moveTimeline.Clips.OrderBy(clip => clip.StartTime))
            {
                int durationTicks = ResolveMoveDurationTicks(package.HandCoachMoves, moveClip.MoveId, moveTimeline, moveClip);
                document.Moves.Add(new JDNextMoveClip
                {
                    Time = TimelineTicksToSeconds(package.TimelineStructure, moveClip.StartTime),
                    Duration = TimelineTicksToDurationSeconds(package.TimelineStructure, moveClip.StartTime, durationTicks),
                    Name = moveClip.MoveId,
                    GoldMove = moveClip.IsGoldMove ? 1 : 0,
                    CoachID = moveTimeline.CoachId
                });
            }
        }

        return document;
    }

    private async Task MaterializeAssetsAsync(string inputRoot, string outputRoot, JDNextTimelineDocument timelineDocument, CancellationToken cancellationToken)
    {
        string mediaRoot = Path.Combine(inputRoot, "media");
        string menuArtRoot = Path.Combine(inputRoot, "menuart");
        string movesRoot = Path.Combine(inputRoot, "moves");
        string pictosRoot = Path.Combine(inputRoot, "pictos");

        string jdiMovesRoot = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.MovesFolder);
        string jdiPictosRoot = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.PictogramsFolder);
        string jdiVideoRoot = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string jdiAudioRoot = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.AudioFolder);

        Directory.CreateDirectory(jdiMovesRoot);
        Directory.CreateDirectory(jdiPictosRoot);
        Directory.CreateDirectory(jdiVideoRoot);
        Directory.CreateDirectory(jdiAudioRoot);

        _logger.LogInformation("Importing JDNext PC move assets");

        if (Directory.Exists(movesRoot))
        {
            foreach (string moveFile in Directory.EnumerateFiles(movesRoot, "*.msm", SearchOption.TopDirectoryOnly))
                File.Copy(moveFile, Path.Combine(jdiMovesRoot, Path.GetFileName(moveFile)), true);
        }

        _logger.LogInformation("Importing JDNext PC pictograms");
        if (Directory.Exists(pictosRoot))
        {
            foreach (string pictoId in timelineDocument.Pictos.Select(picto => picto.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string sourcePath = Path.Combine(pictosRoot, $"{pictoId}.png");
                if (!File.Exists(sourcePath))
                    continue;

                await using FileStream input = File.OpenRead(sourcePath);
                string destinationPath = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.PictogramFile(pictoId));
                await _textureService.ConvertTextureAsync(input, destinationPath, cancellationToken);
            }
        }

        await ImportMenuArtAsync(menuArtRoot, outputRoot, cancellationToken);

        if (Directory.Exists(mediaRoot))
        {
            _logger.LogInformation("Importing JDNext PC media assets");

            string? videoPath = SelectLargestFile(mediaRoot, ["*.webm", "*.mp4", "*.mkv", "*.mov"]);
            if (videoPath is not null)
            {
                _logger.LogInformation("Copying video asset '{VideoFile}' into JDI package", Path.GetFileName(videoPath));
                File.Copy(videoPath, Path.Combine(jdiVideoRoot, Path.GetFileName(videoPath)), true);
            }

            string? audioPath = SelectLargestFile(mediaRoot, ["*.ogg", "*.opus", "*.wav"]);
            if (audioPath is not null)
            {
                _logger.LogInformation("Converting JDNext PC audio '{AudioFile}' to master.opus", Path.GetFileName(audioPath));
                await _mediaProcessor.EnsureInitializedAsync(cancellationToken);
                await _mediaProcessor.ConvertAsync(
                    audioPath,
                    IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.AudioMasterFile),
                    ["-c:a libopus", "-ar 48000"],
                    cancellationToken);
            }
        }
    }

    private async Task ExportAssetsAsync(IntermediateSongPackage package, string packageRoot, string outputRoot, CancellationToken cancellationToken)
    {
        string mediaRoot = Path.Combine(outputRoot, "media");
        string menuArtRoot = Path.Combine(outputRoot, "menuart");
        string movesRoot = Path.Combine(outputRoot, "moves");
        string pictosRoot = Path.Combine(outputRoot, "pictos");

        Directory.CreateDirectory(mediaRoot);
        Directory.CreateDirectory(menuArtRoot);
        Directory.CreateDirectory(movesRoot);
        Directory.CreateDirectory(pictosRoot);

        string songFileName = NormalizeFolderName(package.Metadata.MapName);
        string videoRoot = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string audioPath = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        string targetVideoPath = Path.Combine(mediaRoot, $"{songFileName}.webm");

        _logger.LogInformation("Exporting JDNext PC menu art");
        await ExportMenuArtAsync(package, packageRoot, menuArtRoot, cancellationToken);

        string? sourceVideo = SelectLargestFile(videoRoot, ["*.webm", "*.mp4", "*.mkv", "*.mov"]);
        if (sourceVideo is not null)
        {
            _logger.LogInformation("Preparing JDNext PC video from '{VideoFile}'", Path.GetFileName(sourceVideo));
            await EnsureVp8WebmAsync(sourceVideo, targetVideoPath, cancellationToken);
        }
        else
        {
            _logger.LogWarning("JDNext PC export: no video asset found in '{VideoRoot}'", videoRoot);
        }

        if (File.Exists(audioPath))
        {
            _logger.LogInformation("Converting JDI master audio to JDNext PC ogg");
            await _mediaProcessor.EnsureInitializedAsync(cancellationToken);
            await _mediaProcessor.ConvertAsync(
                audioPath,
                Path.Combine(mediaRoot, $"{songFileName}.ogg"),
                ["-c:a libvorbis", "-ar 48000"],
                cancellationToken);
        }
        else
        {
            _logger.LogWarning("JDNext PC export: master.opus not found at '{AudioPath}'", audioPath);
        }

        HashSet<string> moveIds = new(
            package.CoachTimelines
                .SelectMany(timeline => timeline.Clips)
                .Select(clip => clip.MoveId),
            StringComparer.OrdinalIgnoreCase);

        string sourceMovesRoot = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);
        _logger.LogInformation("Exporting {MoveCount} JDNext PC move assets", moveIds.Count);
        foreach (string moveId in moveIds)
        {
            string sourceMovePath = Path.Combine(sourceMovesRoot, $"{moveId}.msm");
            if (!File.Exists(sourceMovePath))
            {
                _logger.LogWarning("JDNext PC export: move asset not found for {MoveId}", moveId);
                continue;
            }

            File.Copy(sourceMovePath, Path.Combine(movesRoot, $"{moveId.ToLowerInvariant()}.msm"), true);
        }

        _logger.LogInformation("Exporting JDNext PC pictograms");
        foreach (string pictoId in package.Pictograms.Clips.Select(clip => clip.PictogramId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string sourcePictoPath = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.PictogramFile(pictoId));
            if (!File.Exists(sourcePictoPath))
            {
                _logger.LogWarning("JDNext PC export: pictogram asset not found for {PictogramId}", pictoId);
                continue;
            }

            await using FileStream input = File.OpenRead(sourcePictoPath);
            await _textureService.ConvertTextureAsync(input, Path.Combine(pictosRoot, $"{pictoId}.png"), cancellationToken);
        }
    }

    private async Task ImportMenuArtAsync(string menuArtRoot, string outputRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(menuArtRoot))
            return;

        _logger.LogInformation("Importing JDNext PC menu art");

        await ImportMenuArtTextureAsync(Path.Combine(menuArtRoot, "cover.png"), IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.CoverFile), cancellationToken);
        await ImportMenuArtTextureAsync(Path.Combine(menuArtRoot, "title.png"), IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.SongTitleFile), cancellationToken);
        await ImportMenuArtTextureAsync(Path.Combine(menuArtRoot, "bkg.png"), IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.MapBackgroundFile), cancellationToken);

        for (int coachIndex = 1; coachIndex <= 4; coachIndex++)
        {
            string sourceCoach = Path.Combine(menuArtRoot, $"coach{coachIndex:D2}.png");
            string destinationCoach = IntermediatePackageLayout.Resolve(outputRoot, IntermediatePackageLayout.Assets.CoachFile(coachIndex));
            await ImportMenuArtTextureAsync(sourceCoach, destinationCoach, cancellationToken);
        }
    }

    private async Task ImportMenuArtTextureAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            return;

        await using FileStream input = File.OpenRead(sourcePath);
        await _textureService.ConvertTextureAsync(input, destinationPath, cancellationToken);
    }

    private async Task ExportMenuArtAsync(IntermediateSongPackage package, string packageRoot, string menuArtRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(menuArtRoot);

        await ExportMenuArtTextureAsync(
            IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoverFile),
            Path.Combine(menuArtRoot, "cover.png"),
            required: false,
            cancellationToken);

        await ExportMenuArtTextureAsync(
            IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.SongTitleFile),
            Path.Combine(menuArtRoot, "title.png"),
            required: false,
            cancellationToken);

        string backgroundSource = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.MapBackgroundFile);
        if (!File.Exists(backgroundSource))
            backgroundSource = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.AlbumBackgroundFile);

        await ExportMenuArtTextureAsync(
            backgroundSource,
            Path.Combine(menuArtRoot, "bkg.png"),
            required: false,
            cancellationToken);

        for (int coachIndex = 1; coachIndex <= package.Metadata.CoachCount; coachIndex++)
        {
            await ExportMenuArtTextureAsync(
                IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoachFile(coachIndex)),
                Path.Combine(menuArtRoot, $"coach{coachIndex:D2}.png"),
                required: false,
                cancellationToken);
        }
    }

    private async Task ExportMenuArtTextureAsync(string sourcePath, string destinationPath, bool required, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            if (required)
                _logger.LogWarning("JDNext PC export: required menu art source missing at '{SourcePath}'", sourcePath);
            return;
        }

        await using FileStream input = File.OpenRead(sourcePath);
        await _textureService.ConvertTextureAsync(input, destinationPath, cancellationToken);
    }

    private async Task EnsureVp8WebmAsync(string sourceVideoPath, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new System.InvalidOperationException($"Could not determine the directory for '{destinationPath}'."));

        if (LooksLikeVp8Webm(sourceVideoPath))
        {
            _logger.LogInformation("Video '{VideoFile}' is already VP8 WebM; copying directly", Path.GetFileName(sourceVideoPath));
            File.Copy(sourceVideoPath, destinationPath, true);
            return;
        }

        _logger.LogInformation("Video '{VideoFile}' is not VP8 WebM; transcoding to VP8", Path.GetFileName(sourceVideoPath));
        await _mediaProcessor.EnsureInitializedAsync(cancellationToken);
        await _mediaProcessor.ConvertAsync(
            sourceVideoPath,
            destinationPath,
            ["-c:v libvpx", "-b:v 0", "-crf 10", "-pix_fmt yuv420p", "-an"],
            cancellationToken);
    }

    private static bool LooksLikeVp8Webm(string path)
    {
        ReadOnlySpan<byte> pattern = "V_VP8"u8;
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[8192];
        int overlap = pattern.Length - 1;
        int preserved = 0;

        while (true)
        {
            int read = stream.Read(buffer, preserved, buffer.Length - preserved);
            if (read <= 0)
                return false;

            int total = preserved + read;
            for (int index = 0; index <= total - pattern.Length; index++)
            {
                if (buffer.AsSpan(index, pattern.Length).SequenceEqual(pattern))
                    return true;
            }

            if (total < overlap)
            {
                preserved = total;
            }
            else
            {
                buffer.AsSpan(total - overlap, overlap).CopyTo(buffer);
                preserved = overlap;
            }
        }
    }

    private static string? SelectLargestFile(string directory, IReadOnlyList<string> patterns)
    {
        if (!Directory.Exists(directory))
            return null;

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.Length)
            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static int ResolveMoveDurationTicks(
        IReadOnlyDictionary<string, CoachMoveDefinition> moveDefinitions,
        string moveId,
        MoveTimeline timeline,
        MoveClip clip)
    {
        if (moveDefinitions.TryGetValue(moveId, out CoachMoveDefinition? definition))
            return definition.Duration;

        int index = timeline.Clips.FindIndex(candidate => ReferenceEquals(candidate, clip) || candidate.Id == clip.Id);
        if (index >= 0 && index < timeline.Clips.Count - 1)
            return Math.Max(1, timeline.Clips[index + 1].StartTime - clip.StartTime);

        return TimelineTicksPerBeat;
    }

    private static string BuildSuggestedOutputFolder(string outputRoot, string mapName)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output path is required.", nameof(outputRoot));

        return Path.Combine(outputRoot, NormalizeFolderName(mapName));
    }

    private static void PrepareMaterializedDirectory(string outputRoot)
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, true);
        Directory.CreateDirectory(outputRoot);
    }

    private static string NormalizeFolderName(string mapName) =>
        string.IsNullOrWhiteSpace(mapName) ? "song" : mapName.Trim().ToLowerInvariant();

    private static double TimelineTicksToSeconds(TimelineStructureDocument structure, int startTime) =>
        structure.GetSecondsAtBeat(startTime / (double)TimelineTicksPerBeat);

    private static double TimelineTicksToDurationSeconds(TimelineStructureDocument structure, int startTime, int duration)
    {
        double startBeat = startTime / (double)TimelineTicksPerBeat;
        double endBeat = (startTime + duration) / (double)TimelineTicksPerBeat;
        return Math.Max(0, structure.GetSecondsAtBeat(endBeat) - structure.GetSecondsAtBeat(startBeat));
    }

    private static int SecondsToTimelineTicks(TimelineStructureDocument structure, double seconds) =>
        (int)Math.Round(structure.GetBeatAtSeconds(seconds) * TimelineTicksPerBeat);

    private static int DurationSecondsToTimelineTicks(TimelineStructureDocument structure, double startSeconds, double durationSeconds)
    {
        double startBeat = structure.GetBeatAtSeconds(startSeconds);
        double endBeat = structure.GetBeatAtSeconds(startSeconds + durationSeconds);
        return Math.Max(1, (int)Math.Round((endBeat - startBeat) * TimelineTicksPerBeat));
    }

    private static string ToHexColor(IReadOnlyList<double> rgba)
    {
        if (rgba.Count < 4)
            return "#FFFFFFFF";

        byte r = ToByte(rgba[0]);
        byte g = ToByte(rgba[1]);
        byte b = ToByte(rgba[2]);
        byte a = ToByte(rgba[3]);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static IReadOnlyList<double> ToNormalizedColor(string hex)
    {
        string value = (hex ?? string.Empty).TrimStart('#');
        if (value.Length != 8)
            return [1, 1, 1, 1];

        byte r = Convert.ToByte(value[0..2], 16);
        byte g = Convert.ToByte(value[2..4], 16);
        byte b = Convert.ToByte(value[4..6], 16);
        byte a = Convert.ToByte(value[6..8], 16);

        return [r / 255d, g / 255d, b / 255d, a / 255d];
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static T ReadJson<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}.");
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new System.InvalidOperationException($"Could not determine the directory for '{path}'."));
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private sealed class JDNextSongDescription
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Credits { get; set; } = string.Empty;
        public int JDVersion { get; set; }
        public int NumCoach { get; set; }
        public int Difficulty { get; set; }
        public IReadOnlyList<double> LyricColor { get; set; } = [1, 1, 1, 1];
    }

    private sealed class JDNextTimelineDocument
    {
        public IReadOnlyList<double> LyricColor { get; set; } = [1, 1, 1, 1];
        public List<JDNextLyricClip> Lyrics { get; set; } = [];
        public List<JDNextPictoClip> Pictos { get; set; } = [];
        public List<JDNextMoveClip> Moves { get; set; } = [];
    }

    private sealed class JDNextLyricClip
    {
        public double Time { get; set; }
        public double Duration { get; set; }
        public string Text { get; set; } = string.Empty;
        public int IsLineEnding { get; set; }
    }

    private sealed class JDNextPictoClip
    {
        public double Time { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JDNextMoveClip
    {
        public double Time { get; set; }
        public double Duration { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GoldMove { get; set; }
        public int CoachID { get; set; }
    }

    private sealed class JDNextMusicTrackDocument
    {
        public double VideoStartTime { get; set; }
        public double VideoEndTime { get; set; }
        public int StartBeat { get; set; }
        public int EndBeat { get; set; }
        public List<double> Beats { get; set; } = [];
    }
}
