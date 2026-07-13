using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Export;

public sealed class UbiArtAssetWriter(
    ILogger<UbiArtAssetWriter> logger,
    IUbiArtExporterFactory? factory = null,
    IMediaProcessor? mediaProcessor = null) : IUbiArtAssetWriter
{
    private readonly IUbiArtExporterFactory _factory = factory ?? new UbiArtExporterFactory();
    private readonly IMediaProcessor _mediaProcessor = mediaProcessor ?? new DefaultMediaProcessor();
    private readonly UbiArtAudioWriter _audioWriter = new();
    private readonly UbiArtEngineContentWriter _engineContentWriter = new();
    private readonly UbiArtRawAssetWriter _rawAssetWriter = new(logger);
    private readonly UbiArtRecordingExporter _recordingExporter = new(logger);
    private readonly UbiArtTextureWriter _textureWriter = new(logger);

    public async Task ExportAsync(
        IntermediateSongPackage package,
        string? materializedRoot,
        string outputFolder,
        UbiArtPlatform platform,
        UbiArtEngineVersion engineVersion,
        IUbiArtLayout? layout = null,
        IFileSystem? io = null)
    {
        layout ??= CreateDefaultLayout(engineVersion);
        IFileSystem fileSystem = io ?? new SystemFileSystem();
        IPlatformExporter platformExporter = _factory.GetPlatformExporter(platform);
        IEngineContentGenerator engineGenerator = _factory.GetEngineContentGenerator(engineVersion, platform);

        UbiArtGameFolderIpkExporter? ipkExporter = null;
        string? stagingFolder = null;
        string assetOutputFolder = outputFolder;
        if (platform != UbiArtPlatform.Uncooked &&
            UbiArtGameFolderIpkExporter.TryCreate(outputFolder, platform, logger, out ipkExporter))
        {
            stagingFolder = Path.Combine(Path.GetTempPath(), "jdi_ubiart_export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);
            assetOutputFolder = stagingFolder;
            logger.LogInformation(
                "Detected UbiArt game folder at '{OutputFolder}'. Staging loose files before IPK repack into '{ArchiveFolder}'.",
                outputFolder,
                ipkExporter!.ArchiveFolder);
        }

        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        string platformRoot = platformExporter.GetPlatformRootFolder(mapNameLower);
        string rawMapWorldBase = layout.GetMapWorldFolder("", mapNameLower, platform, engineVersion);
        string mapWorldBase = Path.Combine(platformRoot, rawMapWorldBase);
        ExportContext context = new(assetOutputFolder, layout, fileSystem, engineVersion, _mediaProcessor);
        UbiArtExportPlan plan = new(
            package,
            materializedRoot,
            platform,
            engineVersion,
            layout,
            platformExporter,
            engineGenerator,
            context,
            mapWorldBase,
            rawMapWorldBase);

        _rawAssetWriter.ConfigureUncookedVideoSource(plan);
        logger.LogInformation("Exporting {MapName} ({Platform}, {Version})...", plan.MapName, platform, engineVersion);

        if (!string.IsNullOrEmpty(materializedRoot))
            await _textureWriter.GenerateColorsAsync(plan);

        try
        {
            List<Task> tasks =
            [
                _audioWriter.WriteAsync(plan),
                _engineContentWriter.WriteAsync(plan),
                _recordingExporter.ExportAsync(plan)
            ];
            if (!string.IsNullOrEmpty(materializedRoot))
            {
                tasks.Add(_textureWriter.WriteAsync(plan));
                tasks.Add(_rawAssetWriter.WriteAsync(plan));
            }

            await Task.WhenAll(tasks);

            if (ipkExporter is not null && stagingFolder is not null)
                await ipkExporter.ApplyAsync(stagingFolder, package, engineVersion);
        }
        finally
        {
            DeleteStagingFolder(stagingFolder);
        }

        logger.LogInformation("Export completed.");
    }

    private static IUbiArtLayout CreateDefaultLayout(UbiArtEngineVersion engineVersion)
        => engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => new JD2014LayoutResolver(),
            UbiArtEngineVersion.JD2015 => new JD2015LayoutResolver(),
            _ => new UbiArtLayoutResolver()
        };

    private void DeleteStagingFolder(string? stagingFolder)
    {
        if (stagingFolder is null || !Directory.Exists(stagingFolder))
            return;

        try
        {
            Directory.Delete(stagingFolder, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete UbiArt export staging folder '{StagingFolder}'.", stagingFolder);
        }
    }
}