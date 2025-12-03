using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.JDI.Manifests;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Serialization;

public static class IntermediatePackageSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void WriteToFolder(IntermediateSongPackage package, string targetFolder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        Directory.CreateDirectory(targetFolder);
        NormalizeCoachTimelinePointers(package);
        NormalizeCoachMoveFiles(package);

        WriteDocument(Path.Combine(targetFolder, "manifest.json"), package.Manifest);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.MetadataFile), package.Metadata);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.AssetsFile), package.AssetCatalog);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.Timelines.StructureFile), package.TimelineStructure);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.Timelines.LyricsFile), package.Lyrics);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.Timelines.PictogramsFile), package.Pictograms);
        WriteDocument(ResolvePath(targetFolder, package.Manifest.Timelines.EventsFile), package.Events);

        foreach (CoachTimelinePointer pointer in package.Manifest.Timelines.CoachTimelines)
        {
            CoachTimelineDocument? document = package.CoachTimelines.FirstOrDefault(ct => ct.CoachId == pointer.CoachId);
            if (document != null)
                WriteDocument(ResolvePath(targetFolder, pointer.File), document);
        }

        foreach (CoachTimelinePointer pointer in package.Manifest.Timelines.FullBodyCoachTimelines)
        {
            CoachTimelineDocument? document = package.FullBodyCoachTimelines.FirstOrDefault(ct => ct.CoachId == pointer.CoachId);
            if (document != null)
                WriteDocument(ResolvePath(targetFolder, pointer.File), document);
        }

        WriteCoachMovesIfNeeded(targetFolder, package.Manifest.Timelines.HandMovesFile, package.HandCoachMoves);
        WriteCoachMovesIfNeeded(targetFolder, package.Manifest.Timelines.FullBodyMovesFile, package.FullBodyCoachMoves);
    }

    public static IntermediateSongPackage LoadFromFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string manifestPath = Path.Combine(folder, "manifest.json");
        IntermediatePackageManifest manifest = ReadDocument<IntermediatePackageManifest>(manifestPath);

        IntermediateSongPackage package = new()
        {
            Manifest = manifest,
            Metadata = ReadDocument<IntermediateMetadata>(ResolvePath(folder, manifest.MetadataFile)),
            AssetCatalog = ReadDocument<IntermediateAssetCatalog>(ResolvePath(folder, manifest.AssetsFile)),
            TimelineStructure = ReadDocument<TimelineStructureDocument>(ResolvePath(folder, manifest.Timelines.StructureFile)),
            Lyrics = ReadDocument<LyricsTimelineDocument>(ResolvePath(folder, manifest.Timelines.LyricsFile)),
            Pictograms = ReadDocument<PictogramTimelineDocument>(ResolvePath(folder, manifest.Timelines.PictogramsFile)),
            Events = ReadDocument<EventTimelineDocument>(ResolvePath(folder, manifest.Timelines.EventsFile))
        };

        foreach (CoachTimelinePointer pointer in manifest.Timelines.CoachTimelines)
        {
            CoachTimelineDocument doc = ReadDocument<CoachTimelineDocument>(ResolvePath(folder, pointer.File));
            package.CoachTimelines.Add(doc);
        }

        foreach (CoachTimelinePointer pointer in manifest.Timelines.FullBodyCoachTimelines)
        {
            CoachTimelineDocument doc = ReadDocument<CoachTimelineDocument>(ResolvePath(folder, pointer.File));
            package.FullBodyCoachTimelines.Add(doc);
        }

        LoadCoachMovesInto(package.HandCoachMoves, folder, manifest.Timelines.HandMovesFile);
        LoadCoachMovesInto(package.FullBodyCoachMoves, folder, manifest.Timelines.FullBodyMovesFile);

        return package;
    }

    private static void NormalizeCoachTimelinePointers(IntermediateSongPackage package)
    {
        TimelineManifest manifest = package.Manifest.Timelines;
        manifest.CoachTimelines = NormalizePointers(
            package.CoachTimelines,
            manifest.CoachTimelines,
            manifest.Folder,
            coachId => $"coach_{coachId:D2}.json");

        manifest.FullBodyCoachTimelines = NormalizePointers(
            package.FullBodyCoachTimelines,
            manifest.FullBodyCoachTimelines,
            manifest.Folder,
            coachId => $"coach_{coachId:D2}_fullBody.json");
    }

    private static void NormalizeCoachMoveFiles(IntermediateSongPackage package)
    {
        TimelineManifest manifest = package.Manifest.Timelines;
        manifest.HandMovesFile = NormalizeMovesPath(manifest.HandMovesFile, manifest.Folder, "coach_moves_hand.json", package.HandCoachMoves);
        manifest.FullBodyMovesFile = NormalizeMovesPath(manifest.FullBodyMovesFile, manifest.Folder, "coach_moves_fullBody.json", package.FullBodyCoachMoves);
    }

    private static string? NormalizeMovesPath(
        string? existingPath,
        string manifestFolder,
        string defaultFileName,
        Dictionary<string, CoachMoveDefinition> moves)
    {
        if (moves == null || moves.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(existingPath))
            return existingPath;

        return CombineManifestPath(manifestFolder, defaultFileName);
    }

    private static List<CoachTimelinePointer> NormalizePointers(
        List<CoachTimelineDocument> documents,
        List<CoachTimelinePointer> existingPointers,
        string manifestFolder,
        Func<int, string> fileFactory)
    {
        if (documents.Count == 0)
            return [];

        if (existingPointers.Count == documents.Count)
        {
            List<CoachTimelinePointer> orderedPointers = [.. existingPointers.OrderBy(p => p.CoachId)];
            List<CoachTimelineDocument> orderedDocs = [.. documents.OrderBy(c => c.CoachId)];
            bool matches = true;
            for (int i = 0; i < orderedDocs.Count; i++)
            {
                if (orderedPointers[i].CoachId != orderedDocs[i].CoachId)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return existingPointers;
        }

        return [.. documents
            .OrderBy(ct => ct.CoachId)
            .Select(ct => new CoachTimelinePointer
            {
                CoachId = ct.CoachId,
                File = CombineManifestPath(manifestFolder, fileFactory(ct.CoachId))
            })];
    }

    private static void WriteCoachMovesIfNeeded(
        string root,
        string? relativePath,
        Dictionary<string, CoachMoveDefinition> moves)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || moves.Count == 0)
            return;

        Dictionary<string, CoachMoveDefinition> ordered = moves
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        WriteDocument(ResolvePath(root, relativePath), ordered);
    }

    private static void LoadCoachMovesInto(
        Dictionary<string, CoachMoveDefinition> target,
        string root,
        string? relativePath)
    {
        target.Clear();
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        Dictionary<string, CoachMoveDefinition> moves = ReadDocument<Dictionary<string, CoachMoveDefinition>>(ResolvePath(root, relativePath));
        foreach ((string key, CoachMoveDefinition value) in moves)
            target[key] = value;
    }

    private static void WriteDocument<T>(string path, T document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, document, WriteOptions);
    }

    private static T ReadDocument<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing intermediate document: {path}");
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, ReadOptions)!;
    }

    private static string ResolvePath(string root, string relative)
    {
        string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, normalized);
    }

    private static string CombineManifestPath(string folder, string file)
    {
        string normalizedFolder = folder.TrimEnd('/', '\\');
        if (string.IsNullOrEmpty(normalizedFolder))
            return file.Replace('\\', '/');
        return $"{normalizedFolder}/{file.Replace('\\', '/')}";
    }
}
