using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed class JsonMotionRecordingRepository : IMotionRecordingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> SaveAsync(
        string packageRoot,
        MotionRecordingDocument recording,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.CoachId < 0)
            throw new ArgumentOutOfRangeException(nameof(recording), "Recording coach IDs cannot be negative.");
        if (recording.RecordingId == Guid.Empty)
            recording.RecordingId = Guid.NewGuid();

        string folder = IntermediatePackageLayout.Resolve(
            packageRoot,
            IntermediatePackageLayout.Recordings.CoachFolder(recording.CoachId));
        Directory.CreateDirectory(folder);

        string fileName = CreateFileName(recording);
        string path = GetAvailablePath(folder, fileName);
        await WriteAsync(path, recording, cancellationToken);
        return path;
    }

    public async Task<MotionRecordingDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        MotionRecordingDocument recording;
        await using (FileStream stream = File.OpenRead(path))
        {
            recording = await JsonSerializer.DeserializeAsync<MotionRecordingDocument>(stream, JsonOptions, cancellationToken)
                ?? throw new JsonException($"Failed to deserialize motion recording '{path}'.");
        }

        if (recording.RecordingId == Guid.Empty)
        {
            recording.RecordingId = Guid.NewGuid();
            await WriteAsync(path, recording, cancellationToken);
        }

        return recording;
    }

    public IReadOnlyList<string> ListRecordingFiles(string packageRoot, int? coachId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        string recordingsRoot = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Recordings.Folder);
        if (!Directory.Exists(recordingsRoot))
            return [];

        IEnumerable<string> folders = coachId.HasValue
            ? [IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Recordings.CoachFolder(coachId.Value))]
            : Directory.EnumerateDirectories(recordingsRoot, IntermediatePackageLayout.Recordings.CoachFolderPattern, SearchOption.TopDirectoryOnly);

        return folders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, IntermediatePackageLayout.Recordings.RecordingPattern, SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateFileName(MotionRecordingDocument recording)
    {
        DateTimeOffset timestamp = recording.StartedAtUtc == default
            ? DateTimeOffset.UtcNow
            : recording.StartedAtUtc.ToUniversalTime();

        string stamp = timestamp.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        return $"coach_{recording.CoachId:D2}_{stamp}.json";
    }

    private static string GetAvailablePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
            return path;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int suffix = 2;
        do
        {
            path = Path.Combine(folder, $"{stem}_{suffix++}{extension}");
        }
        while (File.Exists(path));

        return path;
    }

    private static async Task WriteAsync(
        string path,
        MotionRecordingDocument recording,
        CancellationToken cancellationToken)
    {
        string tempPath = path + ".tmp";
        await using (FileStream stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, recording, JsonOptions, cancellationToken);

        File.Move(tempPath, path, overwrite: true);
    }
}