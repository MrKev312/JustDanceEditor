using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Serialization;

public static class IntermediatePackageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void WriteToFolder(IntermediateSongPackage package, string targetFolder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        Directory.CreateDirectory(targetFolder);
        ResetTimelineFolder(targetFolder);

        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.MetadataFile), package.Metadata);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.StructureFile), package.TimelineStructure);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.LyricsFile), package.Lyrics);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.PictogramsFile), package.Pictograms);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.GoldEffectsFile), package.GoldEffects);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.HideUserInterfaceFile), package.HideUserInterface);
        WriteDocument(Resolve(targetFolder, IntermediatePackageLayout.Timelines.VibrationsFile), package.Vibrations);

        WriteCoachTimelines(targetFolder, package.CoachTimelines, isFullBody: false);
        WriteCoachTimelines(targetFolder, package.FullBodyCoachTimelines, isFullBody: true);

        WriteCoachMovesIfNeeded(targetFolder, IntermediatePackageLayout.Timelines.HandMovesFile, package.HandCoachMoves);
        WriteCoachMovesIfNeeded(targetFolder, IntermediatePackageLayout.Timelines.FullBodyMovesFile, package.FullBodyCoachMoves);
    }

    public static IntermediateSongPackage LoadFromFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        IntermediateSongPackage package = new()
        {
            Metadata = ReadDocument<IntermediateMetadata>(Resolve(folder, IntermediatePackageLayout.MetadataFile)),
            TimelineStructure = ReadDocument<TimelineStructureDocument>(Resolve(folder, IntermediatePackageLayout.Timelines.StructureFile)),
            Lyrics = ReadDocument<Timeline<KaraokeClip>>(Resolve(folder, IntermediatePackageLayout.Timelines.LyricsFile)),
            Pictograms = ReadDocument<Timeline<PictogramClip>>(Resolve(folder, IntermediatePackageLayout.Timelines.PictogramsFile)),
            GoldEffects = ReadDocumentOrDefault<Timeline<GoldEffectClip>>(Resolve(folder, IntermediatePackageLayout.Timelines.GoldEffectsFile)),
            HideUserInterface = ReadDocumentOrDefault<Timeline<HideUserInterfaceClip>>(Resolve(folder, IntermediatePackageLayout.Timelines.HideUserInterfaceFile)),
            Vibrations = ReadDocumentOrDefault<Timeline<VibrationClip>>(Resolve(folder, IntermediatePackageLayout.Timelines.VibrationsFile))
        };

        LoadCoachTimelinesInto(package.CoachTimelines, folder, isFullBody: false);
        LoadCoachTimelinesInto(package.FullBodyCoachTimelines, folder, isFullBody: true);

        LoadCoachMovesInto(package.HandCoachMoves, folder, IntermediatePackageLayout.Timelines.HandMovesFile);
        LoadCoachMovesInto(package.FullBodyCoachMoves, folder, IntermediatePackageLayout.Timelines.FullBodyMovesFile);

        return package;
    }

    private static void ResetTimelineFolder(string root)
    {
        string folder = Resolve(root, IntermediatePackageLayout.Timelines.Folder);
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
    }

    private static void WriteCoachTimelines(string root, List<MoveTimeline> documents, bool isFullBody)
    {
        foreach (MoveTimeline document in documents.OrderBy(doc => doc.CoachId))
        {
            string relative = isFullBody
                ? IntermediatePackageLayout.Timelines.FullBodyCoachTimelineFile(document.CoachId)
                : IntermediatePackageLayout.Timelines.CoachTimelineFile(document.CoachId);
            WriteDocument(Resolve(root, relative), document);
        }
    }

    private static void LoadCoachTimelinesInto(List<MoveTimeline> target, string root, bool isFullBody)
    {
        string folder = Resolve(root, IntermediatePackageLayout.Timelines.Folder);
        if (!Directory.Exists(folder))
            return;

        string pattern = isFullBody
            ? IntermediatePackageLayout.Timelines.FullBodyPattern
            : IntermediatePackageLayout.Timelines.CoachPattern;

        List<string> files = [.. Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)];

        foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            MoveTimeline document = ReadDocument<MoveTimeline>(file);
            target.Add(document);
        }
    }

    private static void WriteCoachMovesIfNeeded(string root, string relativePath, Dictionary<string, CoachMoveDefinition> moves)
    {
        string resolved = Resolve(root, relativePath);

        // Always write the file (empty dict if no moves), so it's present in the package
        Dictionary<string, CoachMoveDefinition> ordered = moves
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        WriteDocument(resolved, ordered);
    }

    private static void LoadCoachMovesInto(Dictionary<string, CoachMoveDefinition> target, string root, string relativePath)
    {
        target.Clear();
        string resolved = Resolve(root, relativePath);
        if (!File.Exists(resolved))
            return;

        Dictionary<string, CoachMoveDefinition> moves = ReadDocument<Dictionary<string, CoachMoveDefinition>>(resolved);
        foreach ((string key, CoachMoveDefinition value) in moves)
            target[key] = value;
    }

    private static void WriteDocument<T>(string path, T document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, document, JsonOptions);
    }

    private static T ReadDocument<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing intermediate document: {path}");
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw new JsonException($"Failed to deserialize intermediate document '{path}'.");
    }

    private static T ReadDocumentOrDefault<T>(string path)
        where T : new()
    {
        if (!File.Exists(path))
            return new T();
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? new T();
    }

    private static string Resolve(string root, string relative) => IntermediatePackageLayout.Resolve(root, relative);
}
