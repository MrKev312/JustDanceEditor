using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed record LibraryCatalogEntry(
    string Id,
    ItemType Type,
    int UsageCount,
    MoveDefinitionViewModel? MoveDefinition = null,
    string? AssetPath = null,
    bool HasAsset = true);

public sealed class TimelineLibraryCatalogBuilder
{
    internal IReadOnlyList<LibraryCatalogEntry> Build(TimelineEditorViewModel timeline)
    {
        Dictionary<(ItemType Type, string Id), int> usage = CountUsage(timeline);
        List<LibraryCatalogEntry> entries = [];

        AddMoves(entries, timeline.AvailableHandCoachMoves, ItemType.HandMove, isFullBody: false, timeline, usage);
        AddMoves(entries, timeline.AvailableFullBodyCoachMoves, ItemType.FullBodyMove, isFullBody: true, timeline, usage);
        AddPictograms(entries, timeline, usage);
        return entries;
    }

    private static Dictionary<(ItemType Type, string Id), int> CountUsage(TimelineEditorViewModel timeline)
    {
        Dictionary<(ItemType Type, string Id), int> counts = [];
        foreach (ClipViewModel clip in timeline.Tracks.SelectMany(static track => track.Clips))
        {
            (ItemType Type, string Id)? key = clip switch
            {
                MoveClipViewModel move => (move.IsFullBody ? ItemType.FullBodyMove : ItemType.HandMove, move.MoveId),
                PictogramClipViewModel pictogram => (ItemType.Pictogram, pictogram.PictogramId),
                _ => null
            };
            if (key is { } value && !string.IsNullOrWhiteSpace(value.Id))
                counts[value] = counts.GetValueOrDefault(value) + 1;
        }
        return counts;
    }

    private static void AddMoves(
        List<LibraryCatalogEntry> entries,
        IEnumerable<string> ids,
        ItemType type,
        bool isFullBody,
        TimelineEditorViewModel timeline,
        IReadOnlyDictionary<(ItemType Type, string Id), int> usage)
    {
        foreach (string id in ids.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
        {
            MoveDefinitionViewModel definition = timeline.GetOrRegisterMove(id, isFullBody);
            entries.Add(new(
                id,
                type,
                usage.GetValueOrDefault((type, id)),
                definition,
                HasAsset: definition.HasAsset));
        }
    }

    private static void AddPictograms(
        List<LibraryCatalogEntry> entries,
        TimelineEditorViewModel timeline,
        IReadOnlyDictionary<(ItemType Type, string Id), int> usage)
    {
        string directory = Path.Combine(timeline.RootPath, "assets", "pictograms");
        Dictionary<string, string> assets = FindPictogramAssets(directory);
        HashSet<string> ids = [.. assets.Keys];
        ids.UnionWith(timeline.Tracks
            .SelectMany(static track => track.Clips)
            .OfType<PictogramClipViewModel>()
            .Select(static clip => clip.PictogramId)
            .Where(static id => !string.IsNullOrWhiteSpace(id)));

        foreach (string id in ids.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
        {
            bool hasAsset = assets.TryGetValue(id, out string? path);
            entries.Add(new(
                id,
                ItemType.Pictogram,
                usage.GetValueOrDefault((ItemType.Pictogram, id)),
                AssetPath: path ?? Path.Combine(directory, id + ".webp"),
                HasAsset: hasAsset));
        }
    }

    private static Dictionary<string, string> FindPictogramAssets(string directory)
    {
        Dictionary<string, string> assets = new(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return assets;

        foreach (string path in Directory.EnumerateFiles(directory, "*.webp", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            assets.TryAdd(id, path);
        }
        return assets;
    }
}
