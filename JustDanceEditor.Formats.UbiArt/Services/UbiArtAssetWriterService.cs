using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Export;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Services;

public sealed class UbiArtAssetWriterService(IFileSystem? io, ILoggerFactory loggerFactory) : IUbiArtAssetWriter
{
    private readonly IFileSystem? _io = io;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    // Phase 1: Initialize default implementations
    private readonly IUbiArtExporterFactory _exporterFactory = new UbiArtExporterFactory();
    private readonly ITextureProcessor _textureProcessor = new TextureProcessor();

    public Task ExportAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IUbiArtLayout? layout = null, IFileSystem? io = null)
    {
        // Create instance with proper logger and delegate to it
        ILogger<UbiArtAssetWriter> writerLogger = _loggerFactory.CreateLogger<UbiArtAssetWriter>();

        // Phase 1: Inject factories into the Writer (even if unused by legacy logic yet)
        UbiArtAssetWriter writer = new(writerLogger, _exporterFactory, _textureProcessor);

        return writer.ExportAsync(package, materializedRoot, outputFolder, platform, engineVersion, layout, io ?? _io);
    }
}