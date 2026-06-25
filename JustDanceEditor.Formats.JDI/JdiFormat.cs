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

        if (importResult.MaterializedRoot is null)
            throw new InvalidOperationException("Materialized root is null");

        string outputFolder = ResolveOutputFolder(importResult, jdiRequest);

        _logger?.LogInformation("Writing JDI package for '{MapName}' to '{OutputPath}'", importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song", outputFolder);

        if (IsSameDirectory(importResult.MaterializedRoot, outputFolder))
        {
            await GenerateAllMissingImagesAsync(importResult.MaterializedRoot, importResult.Package, cancellationToken);
            _logger?.LogInformation("JDI package already materialized at '{OutputPath}'", outputFolder);
            return;
        }

        if (IsDirectoryInside(importResult.MaterializedRoot, outputFolder))
            throw new InvalidOperationException("JDI export output path cannot be inside the source package folder.");

        // If the input is temporary, we can move it directly
        if (importResult.MaterializedRootIsTemporary)
        {
            await GenerateAllMissingImagesAsync(importResult.MaterializedRoot, importResult.Package, cancellationToken);
            _logger?.LogDebug("Moving temporary JDI package from '{SourcePath}' to '{OutputPath}'", importResult.MaterializedRoot, outputFolder);
            Directory.Move(importResult.MaterializedRoot, outputFolder);
        }
        // Else we'll need to copy the files
        else
        {
            string exportRoot = CreateTemporaryExportRoot();
            try
            {
                _logger?.LogDebug("Preparing JDI export copy from '{SourcePath}' at '{ExportRoot}'", importResult.MaterializedRoot, exportRoot);
                CopyDirectory(importResult.MaterializedRoot, exportRoot);
                await GenerateAllMissingImagesAsync(exportRoot, importResult.Package, cancellationToken);

                _logger?.LogDebug("Copying JDI package from '{ExportRoot}' to '{OutputPath}'", exportRoot, outputFolder);
                CopyDirectory(exportRoot, outputFolder);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                    Directory.Delete(exportRoot, true);
            }
        }

        _logger?.LogInformation("JDI package write completed at '{OutputPath}'", outputFolder);
    }

    private async Task GenerateAllMissingImagesAsync(string packageRoot, IntermediateSongPackage package, CancellationToken cancellationToken)
    {
        IntermediateImageService imageService = new(
            packageRoot,
            package,
            new SystemFileSystem(),
            null,
            _logger);
        await imageService.GenerateAllMissingImagesAsync(cancellationToken);
    }

    private static string ResolveOutputFolder(JdiImportResult importResult, JdiConversionRequest request)
    {
        string? outputFolder = string.IsNullOrWhiteSpace(importResult.SuggestedOutputFolder)
            ? request.OutputPath
            : importResult.SuggestedOutputFolder;

        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output path is required", nameof(request));

        return outputFolder;
    }

    private static string CreateTemporaryExportRoot()
    {
        string exportRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JdiExport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportRoot);
        return exportRoot;
    }

    private static bool IsSameDirectory(string left, string right) =>
        string.Equals(NormalizeDirectoryPath(left), NormalizeDirectoryPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectoryInside(string parentPath, string candidatePath)
    {
        string parent = NormalizeDirectoryPath(parentPath);
        string candidate = NormalizeDirectoryPath(candidatePath);

        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            return false;

        string parentWithSeparator = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;

        return candidate.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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