using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Services;

public sealed class UbiArtAssetWriterService(IFileSystem? io, ILoggerFactory loggerFactory) : IUbiArtAssetWriter
{
    private readonly IFileSystem? _io = io;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public Task ExportToUncookedAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder, IUbiArtLayout? layout = null, UbiArtContainerStyle containerStyle = UbiArtContainerStyle.Uncooked, UbiArtEngineVersion engineVersion = UbiArtEngineVersion.Modern, IFileSystem? io = null)
    {
        // Create instance with proper logger and delegate to it
        ILogger<UbiArtAssetWriter> writerLogger = _loggerFactory.CreateLogger<UbiArtAssetWriter>();
        UbiArtAssetWriter writer = new(writerLogger);
        return writer.ExportToUncookedAsync(package, materializedRoot, outputFolder, layout, containerStyle, engineVersion, io ?? _io);
    }
}