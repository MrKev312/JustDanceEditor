using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal sealed class ClipTargetResolver
{
    private readonly IReadOnlyList<LegacyCinematicActor> actors;
    private readonly IReadOnlyDictionary<string, HashSet<string>> actorKeysByTapePath;

    private ClipTargetResolver(
        IReadOnlyList<LegacyCinematicActor> actors,
        IReadOnlyDictionary<string, HashSet<string>> actorKeysByTapePath)
    {
        this.actors = actors;
        this.actorKeysByTapePath = actorKeysByTapePath;
    }

    public static ClipTargetResolver Create(LegacyCinematicScene scene)
    {
        Dictionary<string, LegacyCinematicActor> actorsByRawKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyCinematicActor actor in scene.Actors)
            actorsByRawKey.TryAdd(actor.Key, actor);

        Dictionary<string, HashSet<string>> actorKeysByTapePath = new(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyCinematicActor actor in scene.Actors)
        {
            string tapePathKey = BuildTapePathKey(actor, actorsByRawKey);
            if (!actorKeysByTapePath.TryGetValue(tapePathKey, out HashSet<string>? keys))
            {
                keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                actorKeysByTapePath[tapePathKey] = keys;
            }

            keys.Add(actor.Key);
        }

        return new ClipTargetResolver(scene.Actors, actorKeysByTapePath);
    }

    public IEnumerable<string> ResolveActorKeys(ActorTargetPath target, bool includeSubSceneDescendants = false)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        string suffix = $"/{target.Key}";

        foreach (LegacyCinematicActor actor in actors)
        {
            if (MatchesTargetPath(actor.Key, target.Key, suffix))
            {
                keys.Add(actor.Key);
            }
        }

        foreach ((string tapePathKey, HashSet<string> actorKeys) in actorKeysByTapePath)
        {
            if (!MatchesTargetPath(tapePathKey, target.Key, suffix))
            {
                continue;
            }

            foreach (string actorKey in actorKeys)
                keys.Add(actorKey);
        }

        if (includeSubSceneDescendants)
            AddSubSceneDescendants(keys);

        return keys;
    }

    private static bool MatchesTargetPath(string candidateKey, string targetKey, string targetSuffix)
    {
        if (candidateKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase) ||
            candidateKey.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int firstSlash = targetKey.IndexOf('/');
        if (firstSlash <= 0 || firstSlash == targetKey.Length - 1)
            return false;

        string root = targetKey[..firstSlash];
        string tail = targetKey[(firstSlash + 1)..];
        return candidateKey.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase) &&
            candidateKey.EndsWith($"/{tail}", StringComparison.OrdinalIgnoreCase);
    }

    private void AddSubSceneDescendants(HashSet<string> keys)
    {
        string[] subSceneKeys = [.. keys
            .Select(key => actors.FirstOrDefault(actor => actor.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(actor => actor != null && IsSubSceneActor(actor))
            .Select(actor => actor!.Key)];

        foreach (string subSceneKey in subSceneKeys)
        {
            string descendantPrefix = $"{subSceneKey}/";
            foreach (LegacyCinematicActor actor in actors)
            {
                if (actor.Key.StartsWith(descendantPrefix, StringComparison.OrdinalIgnoreCase))
                    keys.Add(actor.Key);
            }
        }
    }

    private static string BuildTapePathKey(
        LegacyCinematicActor actor,
        IReadOnlyDictionary<string, LegacyCinematicActor> actorsByRawKey)
    {
        string[] segments = new string[actor.Path.Count];
        for (int i = 0; i < actor.Path.Count; i++)
        {
            string rawPrefixKey = LegacyCinematicNames.NormalizeKey(actor.Path.Take(i + 1));
            segments[i] = actorsByRawKey.TryGetValue(rawPrefixKey, out LegacyCinematicActor? pathActor)
                ? GetNameForTapePath(pathActor)
                : actor.Path[i];
        }

        return LegacyCinematicNames.NormalizeKey(segments);
    }

    private static string GetNameForTapePath(LegacyCinematicActor actor)
    {
        if (!LegacyBinarySerializer.IsTypeId<LegacyCinematicSubSceneActorBinary>(actor.TypeId) ||
            string.IsNullOrWhiteSpace(actor.SubScenePath))
        {
            return actor.Name;
        }

        string fileName = Path.GetFileNameWithoutExtension(actor.SubScenePath.Replace('\\', '/'));
        if (fileName.EndsWith(".isc", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);

        return string.IsNullOrWhiteSpace(fileName)
            ? actor.Name
            : fileName;
    }

    private static bool IsSubSceneActor(LegacyCinematicActor actor) =>
        LegacyBinarySerializer.IsTypeId<LegacyCinematicSubSceneActorBinary>(actor.TypeId) ||
        actor.SubScenePath != null;
}