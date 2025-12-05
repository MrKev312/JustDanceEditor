using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Utilities;

namespace JustDanceEditor.Formats.JDI;

public sealed class JdiFormat : IJdiFormat
{
    public string DisplayName => "JDI";
    public bool CanImport => true;
    public bool CanExport => true;

    public Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputPath) || !Directory.Exists(request.InputPath))
            throw new FileNotFoundException("Input folder not found", request.InputPath);

        IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(request.InputPath);
        JdiConversionHelpers.EnsureSongName(request, package, allowFallbackToMetadata: true);

        return Task.FromResult(new JdiImportResult(
            package,
            "JDI",
            request.InputPath,
            MaterializedRootIsTemporary: false));
    }

    public Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importResult);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required", nameof(request.OutputPath));

        string songName = !string.IsNullOrWhiteSpace(importResult.Package.Metadata.MapName)
            ? importResult.Package.Metadata.MapName
            : (request.SongName ?? "UnknownSong");

        if (importResult.MaterializedRoot is null)
            throw new InvalidOperationException("Materialized root is null");
        if (importResult.MaterializedRoot == request.OutputPath)
            throw new InvalidOperationException("Input and output paths cannot be the same");

        string? suggestedOutput = importResult.SuggestedOutputFolder;
        if (string.IsNullOrWhiteSpace(suggestedOutput))
            throw new InvalidOperationException("Suggested output folder is required for JDI exports.");

        if (string.Equals(importResult.MaterializedRoot, suggestedOutput, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        // If the input is temporary, we can move it directly
        if (importResult.MaterializedRootIsTemporary)
        {
            Directory.Move(importResult.MaterializedRoot, suggestedOutput);
        }
        // Else we'll need to copy the files
        else
        {
            CopyDirectory(importResult.MaterializedRoot, suggestedOutput);
        }

        return Task.CompletedTask;
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