using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnitySongPreviewProvider(ILogger<UnitySongPreviewProvider> logger) : ISongPreviewProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public string FormatName => "Unity";
    public int Priority => 15;

    public bool CanPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "SongInfo.json"));
    }

    public async Task<SongPreviewResult> LoadPreviewAsync(SongPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string songInfoPath = Path.Combine(request.InputPath, "SongInfo.json");
        if (!File.Exists(songInfoPath))
            throw new FileNotFoundException("SongInfo.json is required for Unity song previews.", songInfoPath);

        string previewRoot = Path.Combine(request.WorkingRoot, "unity-preview");
        RecreateDirectory(previewRoot);

        ServerSongJSON songInfo = await ReadSongInfoAsync(songInfoPath, cancellationToken);
        IntermediateSongPackage package = new()
        {
            Metadata = BuildMetadata(songInfo, request.InputPath),
            TimelineStructure = BuildPreviewTimelineStructure(songInfo)
        };

        IntermediatePackageSerializer.WriteToFolder(package, previewRoot);
        TryExtractCover(request.InputPath, previewRoot);
        Task assetWarmupTask = Task.Run(
            () => UnityAssetMaterializer.TryExtractPreviewCoachAssets(request.InputPath, previewRoot, logger),
            CancellationToken.None);

        return new SongPreviewResult(
            package,
            FormatName,
            previewRoot,
            MaterializedRootIsTemporary: true,
            assetWarmupTask);
    }

    private static async Task<ServerSongJSON> ReadSongInfoAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ServerSongJSON>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Failed to deserialize SongInfo.json located at '{path}'.");
    }

    private static IntermediateMetadata BuildMetadata(ServerSongJSON songInfo, string inputPath)
    {
        IntermediateMetadata metadata = (IntermediateMetadata)songInfo;
        string folderName = Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (metadata.SongID == Guid.Empty)
            metadata.SongID = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(metadata.MapName))
            metadata.MapName = string.IsNullOrWhiteSpace(folderName) ? "UnitySong" : folderName;
        if (string.IsNullOrWhiteSpace(metadata.ParentMapName))
            metadata.ParentMapName = metadata.MapName;
        if (string.IsNullOrWhiteSpace(metadata.Title))
            metadata.Title = metadata.MapName;
        if (metadata.CoachCount < 0)
            metadata.CoachCount = 0;

        if (metadata.CoachNamesLocIds is not null && metadata.CoachNamesLocIds.Length != metadata.CoachCount)
            metadata.CoachNamesLocIds = null;

        metadata.AdditionalMetadata["unity.previewOnly"] = bool.TrueString;
        return metadata;
    }

    private static TimelineStructureDocument BuildPreviewTimelineStructure(ServerSongJSON songInfo)
    {
        int markerCount = Math.Max(2, (int)Math.Ceiling(Math.Max(0, songInfo.MapLength) * 2) + 1);
        List<int> markers = new(markerCount);
        for (int index = 0; index < markerCount; index++)
            markers.Add(index * 24_000);

        int endBeat = markerCount - 1;
        return new TimelineStructureDocument
        {
            Markers = markers,
            StartBeat = 0,
            EndBeat = endBeat,
            PreviewEntryBeat = 0,
            PreviewLoopStartBeat = 0,
            PreviewLoopEndBeat = Math.Min(endBeat, 60),
            PreviewDuration = 30,
            Signatures = [new SignatureSegment { Beats = 4, Marker = 0 }],
            Sections = []
        };
    }

    private void TryExtractCover(string inputRoot, string previewRoot)
    {
        string coverDestination = IntermediatePackageLayout.Resolve(previewRoot, IntermediatePackageLayout.Assets.CoverFile);
        try
        {
            if (!UnityAssetMaterializer.TryExtractSingleImageAsset(UnityServerLayout.GetBundleFolder(inputRoot, "Cover"), coverDestination))
                logger.LogDebug("Unity preview did not find cover art in '{InputRoot}'.", inputRoot);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unity preview cover extraction failed for '{InputRoot}'.", inputRoot);
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }
}
