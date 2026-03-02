using JustDanceEditor.Audio;
using JustDanceEditor.Audio.Providers;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Creates a new JDI song package on disk from user-provided dialog data.
/// Handles audio processing (trimming/padding), marker generation, and 
/// writing all required JSON files + placeholder assets.
/// </summary>
public static class NewSongPackageCreator
{

    /// <summary>
    /// Creates a complete JDI package on disk from the NewSongResult data.
    /// Returns the root folder path and the loaded package.
    /// </summary>
    public static async Task<(string rootPath, IntermediateSongPackage package)> CreatePackageAsync(
        NewSongResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Create folder structure
        string rootPath = Path.Combine(result.OutputFolder, result.MapName);
        Directory.CreateDirectory(rootPath);

        // Build markers and structure
        double beatDurationSeconds = 60.0 / result.Bpm;

        int startBeat = result.StartBeat;  // e.g. -8 (user-provided)
        int endBeat = result.EndBeat;      // e.g. 400 (user-provided)

        int markerCount = endBeat - startBeat + 1;
        List<int> markers = SongStructureBuilder.BuildMarkers(result.Bpm, startBeat, endBeat);

        List<SectionSegment> sections = SongStructureBuilder.BuildSections(result.Sections);

        List<SignatureSegment> signatures = SongStructureBuilder.BuildDefaultSignatures(result.BeatsPerMeasure);

        // Build the structure document
        TimelineStructureDocument structure = new()
        {
            Markers = markers,
            Signatures = signatures,
            Sections = sections,
            StartBeat = startBeat,
            EndBeat = endBeat,
            VideoStartOffset = 0,
            PreviewEntryBeat = 0,
            PreviewLoopStartBeat = Math.Max(0, 16),
            PreviewLoopEndBeat = Math.Min(endBeat - 1, 16 + 64),
            PrevewDuration = 30
        };

        // Build metadata
        IntermediateMetadata metadata = new()
        {
            SongID = Guid.NewGuid(),
            MapName = result.MapName,
            Title = result.Title,
            Artist = result.Artist,
            CoachCount = result.CoachCount,
            Difficulty = result.Difficulty,
            SweatDifficulty = 1,
            OriginalJDVersion = 2025,
            LyricsColor = "#FFFFFFFF",
            Tags = [],
            Status = 0,
            MojoValue = 0,
            CountInProgression = 0,
            MapLengthSeconds = markerCount * beatDurationSeconds
        };

        // Build coach timelines (one empty per coach)
        List<MoveTimeline> coachTimelines = [];
        for (int i = 0; i < result.CoachCount; i++)
        {
            coachTimelines.Add(new MoveTimeline
            {
                CoachId = i,
                TrackId = i + 1
            });
        }

        // Assemble the package
        IntermediateSongPackage package = new()
        {
            Metadata = metadata,
            TimelineStructure = structure,
            Lyrics = new Timeline<KaraokeClip>(),
            Pictograms = new Timeline<PictogramClip>(),
            GoldEffects = new Timeline<GoldEffectClip>(),
            HideUserInterface = new Timeline<HideUserInterfaceClip>(),
            CoachTimelines = coachTimelines
        };

        // Write all JSON files
        IntermediatePackageSerializer.WriteToFolder(package, rootPath);

        // Process and write audio
        await ProcessAudioAsync(result, rootPath, startBeat, beatDurationSeconds, ct, endBeat);

        // Create placeholder asset directories and images
        CreatePlaceholderAssets(rootPath, result.CoachCount);

        return (rootPath, package);
    }

    /// <summary>
    /// Process the source audio file: trim/pad to span from startBeat through endBeat.
    /// In the processed audio, beat 0 is at |startBeat| * beatDuration.
    /// The file is padded with silence at the end if the original audio doesn't reach endBeat.
    /// </summary>
    private static async Task ProcessAudioAsync(
        NewSongResult result,
        string rootPath,
        int startBeat,
        double beatDurationSeconds,
        CancellationToken ct,
        int endBeat)
    {
        string audioDir = Path.Combine(rootPath, "assets", "audio");
        Directory.CreateDirectory(audioDir);
        string masterPath = Path.Combine(rootPath, IntermediatePackageLayout.Assets.AudioMasterFile
            .Replace('/', Path.DirectorySeparatorChar));

        // Convert source audio to WAV first (handles Opus and all other formats via FFmpeg)
        string tempWav = await AudioConversionService.ConvertToWavAsync(result.AudioFilePath);

        try
        {
            await Task.Run(() =>
            {
                // In the original audio file, beat 0 is at ZeroBeatTimeSeconds.
                // startBeat (e.g. -8) occurs at ZeroBeatTime + startBeat * beatDuration in the original.
                // endBeat occurs at ZeroBeatTime + endBeat * beatDuration in the original.
                // The processed audio should span from startBeat to endBeat.
                double audioStartTime = result.ZeroBeatTimeSeconds + (startBeat * beatDurationSeconds);
                double desiredDuration = (endBeat - startBeat) * beatDurationSeconds;

                using AudioFileReader reader = new(tempWav);
                double sourceDuration = reader.TotalTime.TotalSeconds;
                ISampleProvider source = (ISampleProvider)reader;

                // Step 1: Handle the start — trim or pad
                if (audioStartTime > 0)
                {
                    // The start of our range is inside the audio file — skip into it
                    source = new OffsetSampleProvider(source)
                    {
                        SkipOver = TimeSpan.FromSeconds(audioStartTime)
                    };
                }
                else if (audioStartTime < 0)
                {
                    // The start of our range is before the audio file — pad with silence
                    source = new OffsetSampleProvider(source)
                    {
                        DelayBy = TimeSpan.FromSeconds(-audioStartTime)
                    };
                }

                // Step 2: Handle the end — pad with silence if needed
                // How much audio is available after audioStartTime?
                double availableFromStart = sourceDuration - Math.Max(0, audioStartTime);
                // If audioStartTime < 0, we added silence at the start, so total available becomes:
                double totalAvailable = audioStartTime < 0
                    ? -audioStartTime + sourceDuration  // silence padding + full file
                    : availableFromStart;               // from trim point to end

                if (totalAvailable < desiredDuration)
                {
                    double silenceNeeded = desiredDuration - totalAvailable;
                    source = new OffsetSampleProvider(source)
                    {
                        LeadOut = TimeSpan.FromSeconds(silenceNeeded)
                    };
                }

                // Step 3: Truncate to exactly the desired duration using a limiting wrapper
                source = new TruncatingSampleProvider(source, desiredDuration);

                // Encode to Opus
                using FileStream fs = File.Create(masterPath);
                OpusEncoderHelper.EncodeToOpus(source, fs);
            }, ct);
        }
        finally
        {
            // Clean up temp WAV
            try
            {
                File.Delete(tempWav);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Creates placeholder directories and minimal placeholder files 
    /// for required asset locations.
    /// </summary>
    private static void CreatePlaceholderAssets(string rootPath, int coachCount)
    {
        // Create all required asset directories
        string[] directories =
        [
            IntermediatePackageLayout.Assets.AudioFolder,
            IntermediatePackageLayout.Assets.VideoFolder,
            IntermediatePackageLayout.Assets.PreviewVideoFolder,
            IntermediatePackageLayout.Assets.CoverAssetsFolder,
            IntermediatePackageLayout.Assets.CoachesFolder,
            IntermediatePackageLayout.Assets.BackgroundsFolder,
            IntermediatePackageLayout.Assets.PictogramsFolder,
            IntermediatePackageLayout.Assets.MovesFolder,
            IntermediatePackageLayout.Assets.GesturesFolder
        ];

        foreach (string dir in directories)
        {
            string fullPath = Path.Combine(rootPath, dir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(fullPath);
        }

        // Create simple placeholder WebP images using a minimal 1x1 magenta WebP
        // This ensures the image service can later generate proper placeholders
        byte[] placeholderWebP = CreateMinimalWebP();

        // Cover assets
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.CoverFile, placeholderWebP);
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.SquareCoverFile, placeholderWebP);
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.SongTitleFile, placeholderWebP);

        // Coaches
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.AlbumCoachFile, placeholderWebP);
        for (int i = 1; i <= coachCount; i++)
        {
            WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.CoachFile(i), placeholderWebP);
        }

        // Backgrounds
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.MapBackgroundFile, placeholderWebP);
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.BannerFile, placeholderWebP);
        WritePlaceholder(rootPath, IntermediatePackageLayout.Assets.AlbumBackgroundFile, placeholderWebP);
    }

    private static void WritePlaceholder(string rootPath, string relativePath, byte[] data)
    {
        string fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir != null)
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(fullPath, data);
    }

    /// <summary>
    /// Creates a minimal valid WebP file (1x1 magenta pixel).
    /// This is the smallest valid lossy WebP that image libraries can read.
    /// </summary>
    private static byte[] CreateMinimalWebP()
    {
        // Minimal valid WebP file: RIFF header + VP8 lossy frame for a 1x1 magenta pixel
        // This is a well-known minimal WebP binary
        return
        [
            0x52, 0x49, 0x46, 0x46, // "RIFF"
            0x24, 0x00, 0x00, 0x00, // File size - 8
            0x57, 0x45, 0x42, 0x50, // "WEBP"
            0x56, 0x50, 0x38, 0x20, // "VP8 "
            0x18, 0x00, 0x00, 0x00, // Chunk size
            0x30, 0x01, 0x00, 0x9D, // VP8 bitstream header
            0x01, 0x2A, 0x01, 0x00, // Width=1
            0x01, 0x00, 0x01, 0x40, // Height=1
            0x25, 0xA4, 0x00, 0x03, // Quantization + coefficients
            0x70, 0x00, 0xFE, 0xFB, // Bitstream data
            0x94, 0x00, 0x00        // Padding
        ];
    }
}