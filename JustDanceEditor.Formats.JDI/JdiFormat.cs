using JustDanceEditor.Formats.JDI.Serialization;

namespace JustDanceEditor.Formats.JDI;

public sealed class JdiFormat : IJdiFormat
{
    public string DisplayName => "JDI";
    public bool CanImport => true;
    public bool CanExport => true;

    public Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not JdiConversionRequest jdiRequest)
            throw new ArgumentException("JDI import expects a JdiConversionRequest.", nameof(request));

        if (string.IsNullOrWhiteSpace(jdiRequest.InputPath) || !Directory.Exists(jdiRequest.InputPath))
            throw new FileNotFoundException("Input folder not found", jdiRequest.InputPath);

        IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(jdiRequest.InputPath);

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

    public Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
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