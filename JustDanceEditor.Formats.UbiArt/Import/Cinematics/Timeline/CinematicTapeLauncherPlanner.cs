using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

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
        => CinematicTapeLauncherResolver.GetLauncherVisits(
            tapeCasesByActorKey,
            targetResolver,
            sequenceIndices,
            parentVisit,
            clip,
            label => ResolveFallbackTapePath(fileSystem, label),
            logger);

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

    internal static IReadOnlyList<TapeVisit> FindSequenceVisits(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicScene scene,
        string label,
        ILogger logger)
    {
        return
        [
            .. BuildTapeCaseDefinitions(fileSystem, scene, logger)
                .Values
                .SelectMany(definition => definition.PathsByLabel)
                .Where(entry => entry.Key.Equals(label, StringComparison.OrdinalIgnoreCase))
                .Select(entry => CinematicNames.NormalizePath(entry.Value))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new TapeVisit(path, 0))
        ];
    }

    private static string? ResolveFallbackTapePath(
        JustDanceUbiArtFileSystem fileSystem,
        string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        string candidate = CinematicNames.NormalizePath(label.EndsWith(".tape", StringComparison.OrdinalIgnoreCase)
            ? label
            : Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{label}.tape"));
        if (!fileSystem.GetFilePath(candidate, out _))
            return null;

        return candidate;
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
