using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed class JsonMotionTrainingSelectionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<MotionTrainingSelectionDocument> LoadAsync(
        string packageRoot,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(packageRoot);
        if (!File.Exists(path))
            return new MotionTrainingSelectionDocument();

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MotionTrainingSelectionDocument>(stream, JsonOptions, cancellationToken)
            ?? new MotionTrainingSelectionDocument();
    }

    public async Task SaveAsync(
        string packageRoot,
        MotionTrainingSelectionDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        string path = GetPath(packageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Could not determine the directory for '{path}'."));

        if (document.Exclusions.Count == 0)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetPath(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        return IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Recordings.MsmTrainingFile);
    }
}