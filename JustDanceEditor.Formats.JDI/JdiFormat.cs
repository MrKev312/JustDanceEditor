using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.JDI;

public sealed class JdiFormat : IJdiFormat
{
    private readonly ILogger<JdiFormat>? _logger;

    public JdiFormat()
    {
    }

    public JdiFormat(ILogger<JdiFormat> logger)
    {
        _logger = logger;
    }

    public string DisplayName => "JDI";
    public bool CanImport => true;
    public bool CanExport => true;

    public Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not JdiConversionRequest jdiRequest)
            throw new ArgumentException("JDI import expects a JdiConversionRequest.", nameof(request));

        if (string.IsNullOrWhiteSpace(jdiRequest.InputPath) || !Directory.Exists(jdiRequest.InputPath))
            throw new FileNotFoundException("Input folder not found", jdiRequest.InputPath);

        _logger?.LogInformation("Loading JDI package from '{InputPath}'", jdiRequest.InputPath);
        IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(jdiRequest.InputPath);
        _logger?.LogInformation("Loaded JDI package for '{MapName}'", package.Metadata.MapName ?? package.Metadata.Title ?? "song");

        return Task.FromResult(new JdiImportResult(
            package,
            "JDI",
            jdiRequest.InputPath,
            MaterializedRootIsTemporary: false));
    }

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        string metadata = Path.Combine(path, IntermediatePackageLayout.MetadataFile);
        return File.Exists(metadata);
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not JdiConversionRequest jdiRequest)
            throw new ArgumentException("JDI export expects a JdiConversionRequest.", nameof(request));

        ArgumentNullException.ThrowIfNull(importResult);

        if (string.IsNullOrWhiteSpace(jdiRequest.OutputPath))
            throw new ArgumentException("Output path is required", nameof(jdiRequest.OutputPath));

        if (importResult.MaterializedRoot is null)
            throw new InvalidOperationException("Materialized root is null");

        string? suggestedOutput = importResult.SuggestedOutputFolder;
        if (string.IsNullOrWhiteSpace(suggestedOutput))
            throw new InvalidOperationException("Suggested output folder is required for JDI exports.");

        _logger?.LogInformation("Writing JDI package for '{MapName}' to '{OutputPath}'", importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song", suggestedOutput);

        // Generate all derived assets before exporting
        IntermediateImageService imageService = new(
            importResult.MaterializedRoot,
            importResult.Package,
            new SystemFileSystem(),
            null,
            _logger);
        await imageService.GenerateAllMissingImagesAsync(cancellationToken);

        if (string.Equals(importResult.MaterializedRoot, suggestedOutput, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("JDI package already materialized at '{OutputPath}'", suggestedOutput);
            return;
        }

        // If the input is temporary, we can move it directly
        if (importResult.MaterializedRootIsTemporary)
        {
            _logger?.LogDebug("Moving temporary JDI package from '{SourcePath}' to '{OutputPath}'", importResult.MaterializedRoot, suggestedOutput);
            Directory.Move(importResult.MaterializedRoot, suggestedOutput);
        }
        // Else we'll need to copy the files
        else
        {
            _logger?.LogDebug("Copying JDI package from '{SourcePath}' to '{OutputPath}'", importResult.MaterializedRoot, suggestedOutput);
            CopyDirectory(importResult.MaterializedRoot, suggestedOutput);
        }

        _logger?.LogInformation("JDI package write completed at '{OutputPath}'", suggestedOutput);
    }

    static void CopyDirectory(string sourceDir, string destDir, bool overwrite = true)
    {
        DirectoryInfo dir = new(sourceDir);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destDir);

        // Copy all files
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetFilePath, overwrite);
        }

        // Copy all subdirectories recursively
        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDest = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDest, overwrite);
        }
    }
}
