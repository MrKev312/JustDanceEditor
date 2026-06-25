using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal sealed record CinematicTapeCaseDefinition(IReadOnlyDictionary<string, string> PathsByLabel);

internal static class CinematicTapeLauncherPlanner
{
    internal static IEnumerable<TapeVisit> GetLauncherVisits(
        JustDanceUbiArtFileSystem fileSystem,
        IReadOnlyDictionary<string, CinematicTapeCaseDefinition> tapeCasesByActorKey,
        ClipTargetResolver? targetResolver,
        Dictionary<string, int> sequenceIndices,
        TapeVisit parentVisit,
        TapeClip clip,
        ILogger logger)
    {
        CinematicTapeLauncherClip launcher = clip.TapeLauncher!;
        if (launcher.Action != 0 ||
            launcher.EffectiveTapeLabels.Count == 0 ||
            targetResolver == null ||
            tapeCasesByActorKey.Count == 0)
        {
            yield break;
        }

        IReadOnlyList<string> labels = ResolvePlayLabels(launcher, parentVisit, clip, sequenceIndices);
        if (labels.Count == 0)
            yield break;

        HashSet<string> yieldedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ActorTargetPath target in clip.Targets)
        {
            foreach (string actorKey in targetResolver.ResolveActorKeys(target))
            {
                if (!tapeCasesByActorKey.TryGetValue(actorKey, out CinematicTapeCaseDefinition? tapeCase))
                    continue;

                foreach (string label in labels)
                {
                    if (!TryResolveTapePath(fileSystem, tapeCase, label, out string? tapePath) ||
                        !yieldedPaths.Add(tapePath))
                    {
                        continue;
                    }

                    logger.LogDebug(
                        "Resolved TapeLauncher label '{Label}' on '{TargetKey}' to '{TapePath}' at frame {StartFrame}.",
                        label,
                        actorKey,
                        tapePath,
                        clip.StartFrame);
                    yield return new TapeVisit(
                        tapePath,
                        clip.StartFrame,
                        TargetFilter: parentVisit.TargetFilter,
                        PersistentMaterialState: parentVisit.PersistentMaterialState);
                }
            }
        }
    }

    internal static IReadOnlyDictionary<string, CinematicTapeCaseDefinition> BuildTapeCaseDefinitions(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicScene? scene,
        ILogger logger)
    {
        if (scene == null || scene.Actors.Count == 0)
            return new Dictionary<string, CinematicTapeCaseDefinition>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, CinematicTapeCaseDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ActorTemplate?> templateCache = new(StringComparer.OrdinalIgnoreCase);
        foreach (CinematicActor actor in scene.Actors)
        {
            if (string.IsNullOrWhiteSpace(actor.TemplatePath) ||
                !TryReadTapeCaseActorTemplate(fileSystem, actor.TemplatePath, templateCache, logger, out ActorTemplate? template))
            {
                continue;
            }

            Dictionary<string, string> pathsByLabel = new(StringComparer.OrdinalIgnoreCase);
            foreach (Component component in template.Components)
            {
                foreach (TapesRack rack in component.TapesRack)
                {
                    foreach (Entry entry in rack.Entries)
                    {
                        string path = CinematicNames.NormalizePath(entry.Path);
                        if (string.IsNullOrWhiteSpace(path))
                            continue;

                        AddLabel(pathsByLabel, entry.Label, path);
                        string tapeName = Path.GetFileNameWithoutExtension(GetTapeName(path));
                        AddLabel(pathsByLabel, tapeName, path);
                        AddLabel(pathsByLabel, GetTapeName(path), path);
                    }
                }
            }

            if (pathsByLabel.Count > 0)
                definitions[actor.Key] = new CinematicTapeCaseDefinition(pathsByLabel);
        }

        return definitions;
    }

    private static IReadOnlyList<string> ResolvePlayLabels(
        CinematicTapeLauncherClip launcher,
        TapeVisit parentVisit,
        TapeClip clip,
        Dictionary<string, int> sequenceIndices)
    {
        if (launcher.TapeChoice == 2)
            return launcher.EffectiveTapeLabels;

        if (launcher.EffectiveTapeLabels.Count == 1)
            return launcher.EffectiveTapeLabels;

        string sequenceKey = BuildSequenceKey(parentVisit, clip, launcher);
        if (!sequenceIndices.TryGetValue(sequenceKey, out int index))
            index = 0;

        sequenceIndices[sequenceKey] = index + 1;
        string label = launcher.EffectiveTapeLabels[index % launcher.EffectiveTapeLabels.Count];
        return [label];
    }

    private static string BuildSequenceKey(
        TapeVisit parentVisit,
        TapeClip clip,
        CinematicTapeLauncherClip launcher) =>
        string.Join(
            "|",
            parentVisit.Path,
            clip.DurationFrames.ToString(CultureInfo.InvariantCulture),
            string.Join(";", clip.Targets.Select(target => target.Key)),
            string.Join(";", launcher.EffectiveTapeLabels));

    private static bool TryResolveTapePath(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicTapeCaseDefinition tapeCase,
        string label,
        [NotNullWhen(true)] out string? tapePath)
    {
        tapePath = null;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (tapeCase.PathsByLabel.TryGetValue(label, out tapePath))
            return true;

        string candidate = CinematicNames.NormalizePath(label.EndsWith(".tape", StringComparison.OrdinalIgnoreCase)
            ? label
            : Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{label}.tape"));
        if (!fileSystem.GetFilePath(candidate, out _))
            return false;

        tapePath = candidate;
        return true;
    }

    private static void AddLabel(Dictionary<string, string> pathsByLabel, string? label, string path)
    {
        if (!string.IsNullOrWhiteSpace(label) && !pathsByLabel.ContainsKey(label))
            pathsByLabel[label] = path;
    }

    private static string GetTapeName(string path)
    {
        string normalized = path.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        return fileName.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private static bool TryReadTapeCaseActorTemplate(
        JustDanceUbiArtFileSystem fileSystem,
        string templatePath,
        Dictionary<string, ActorTemplate?> templateCache,
        ILogger logger,
        [NotNullWhen(true)] out ActorTemplate? template)
    {
        string normalizedTemplatePath = CinematicNames.NormalizePath(templatePath);
        if (templateCache.TryGetValue(normalizedTemplatePath, out template))
            return template != null;

        template = null;
        if (!fileSystem.GetFilePath(normalizedTemplatePath, out CookedFile? templateFile))
        {
            templateCache[normalizedTemplatePath] = null;
            return false;
        }

        try
        {
            using Stream stream = fileSystem.GetFileStream(templateFile);
            template = fileSystem.VersionProfile.Serializer != null
                ? fileSystem.VersionProfile.Serializer.Deserialize<ActorTemplate>(stream)
                : JsonSerializer.Deserialize<ActorTemplate>(
                    new StreamReader(stream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'));
            templateCache[normalizedTemplatePath] = template;
            return template != null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentOutOfRangeException)
        {
            logger.LogDebug(ex, "Could not read TapeCase actor template '{TemplatePath}'.", normalizedTemplatePath);
            templateCache[normalizedTemplatePath] = null;
            return false;
        }
    }
}