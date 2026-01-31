using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Export;

public sealed class UbiArtAssetWriterService(IFileSystem? io, ILoggerFactory loggerFactory) : IUbiArtAssetWriter
{
    private readonly IFileSystem? _io = io;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    // Phase 1: Initialize default implementations
    private readonly IUbiArtExporterFactory _exporterFactory = new UbiArtExporterFactory();

    public Task ExportAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IUbiArtLayout? layout = null, IFileSystem? io = null)
    {
        // Create instance with proper logger and delegate to it
        ILogger<UbiArtAssetWriter> writerLogger = _loggerFactory.CreateLogger<UbiArtAssetWriter>();

        // Inject exporter factory
        UbiArtAssetWriter writer = new(writerLogger, _exporterFactory);

        return writer.ExportAsync(package, materializedRoot, outputFolder, platform, engineVersion, layout, io ?? _io);
    }
}