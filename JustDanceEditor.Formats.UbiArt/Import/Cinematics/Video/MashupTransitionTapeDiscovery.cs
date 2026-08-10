using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal sealed record MashupTransitionColorTapeSequence(
    string InitialTapeName,
    IReadOnlyList<string> TransitionTapeNames,
    bool IsAuthored);

internal static class MashupTransitionTapeDiscovery
{
    private const string FallbackInitialColorTape = "color_green.tape";
    private const string UvTapePrefix = "uv_";
    private static readonly string[] FallbackTransitionColorTapes =
    [
        "color_purple.tape",
        "color_blue.tape",
        "color_orange.tape",
        "color_green.tape"
    ];
    private static readonly string[] FallbackUvScrollTapes =
    [
        "uv_left.tape",
        "uv_right.tape"
    ];

    internal static MashupTransitionColorTapeSequence ResolveColorTapeSequence(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        MashupTransitionColorTapeSequence fallback = new(
            FallbackInitialColorTape,
            FallbackTransitionColorTapes,
            IsAuthored: false);

        IReadOnlyList<MashupColorTapeEntry> colorTapeEntries = ReadGenericColorTapeEntries(fileSystem, logger);
        IReadOnlyList<string> launcherSequence = ResolveColorTapeLauncherSequence(fileSystem, colorTapeEntries, logger);
        if (launcherSequence.Count > 0)
        {
            return new MashupTransitionColorTapeSequence(
                ResolveInitialColorTape(colorTapeEntries),
                launcherSequence,
                IsAuthored: true);
        }

        IReadOnlyList<string> rackSequence = BuildColorTapeSequenceFromRack(colorTapeEntries);
        if (rackSequence.Count > 0)
        {
            logger.LogDebug(
                "Using JD2014 mashup color tape order from mu_generic_tapes TapesRack because no authored TapeLauncher sequence was found.");
            return new MashupTransitionColorTapeSequence(
                ResolveInitialColorTape(colorTapeEntries),
                rackSequence,
                IsAuthored: true);
        }

        logger.LogDebug("Could not resolve authored JD2014 mashup color tape order; using legacy fallback color sequence.");
        return fallback;
    }

    internal static IReadOnlyList<string> ResolveUvScrollTapeSequence(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        List<string> tapeNames = [];
        HashSet<string> seenTapeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string templatePath in EnumerateGenericTapeTemplatePaths(fileSystem, logger))
        {
            if (!TryReadActorTemplate(fileSystem, templatePath, logger, out ActorTemplate? actorTemplate))
                continue;

            foreach (Component component in actorTemplate.Components)
            {
                foreach (TapesRack rack in component.TapesRack)
                {
                    foreach (Entry entry in rack.Entries)
                    {
                        string tapeName = GetTapeName(entry.Path);
                        if (!IsUvScrollTapeName(tapeName) || !seenTapeNames.Add(tapeName))
                            continue;

                        tapeNames.Add(tapeName);
                    }
                }
            }

            if (tapeNames.Count > 0)
                return tapeNames;
        }

        return FallbackUvScrollTapes;
    }

    internal static string BuildCinematicsTapePath(string cinematicsFolder, string tapeNameOrPath)
    {
        string normalized = CinematicNames.NormalizePath(tapeNameOrPath);
        if (Path.IsPathRooted(normalized) ||
            normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal))
        {
            return normalized;
        }

        return Path.Combine(cinematicsFolder, normalized);
    }

    internal static string GetTapeName(string path)
    {
        string normalized = CinematicNames.NormalizePath(path).Replace('\\', '/');
        int slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex + 1 < normalized.Length
            ? normalized[(slashIndex + 1)..]
            : normalized;
    }

    private static IReadOnlyList<MashupColorTapeEntry> ReadGenericColorTapeEntries(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        List<MashupColorTapeEntry> entries = [];
        HashSet<string> seenTapeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string templatePath in EnumerateGenericTapeTemplatePaths(fileSystem, logger))
        {
            if (!TryReadActorTemplate(fileSystem, templatePath, logger, out ActorTemplate? actorTemplate))
                continue;

            foreach (Component component in actorTemplate.Components)
            {
                foreach (TapesRack rack in component.TapesRack)
                {
                    foreach (Entry entry in rack.Entries)
                    {
                        string tapeName = GetTapeName(entry.Path);
                        if (!IsColorTapeName(tapeName) || !seenTapeNames.Add(tapeName))
                            continue;

                        string label = string.IsNullOrWhiteSpace(entry.Label)
                            ? Path.GetFileNameWithoutExtension(tapeName)
                            : entry.Label;
                        entries.Add(new MashupColorTapeEntry(label, CinematicNames.NormalizePath(entry.Path)));
                    }
                }
            }

            if (entries.Count > 0)
                return entries;
        }

        return entries;
    }

    private static List<string> EnumerateGenericTapeTemplatePaths(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        string knownTemplatePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", "mu_generic_tapes.tpl");
        List<string> templatePaths = [knownTemplatePath];

        CinematicScene scene;
        try
        {
            scene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            logger.LogDebug(ex, "Could not inspect mashup scene graph for mu_generic_tapes.");
            return templatePaths;
        }

        foreach (CinematicActor actor in scene.Actors)
        {
            if (!string.Equals(actor.Name, "mu_generic_tapes", StringComparison.OrdinalIgnoreCase) &&
                !actor.Key.EndsWith("mu_generic_tapes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(actor.TemplatePath) &&
                !string.Equals(actor.TemplatePath, knownTemplatePath, StringComparison.OrdinalIgnoreCase))
            {
                templatePaths.Add(actor.TemplatePath);
            }
        }

        return templatePaths;
    }

    private static bool TryReadActorTemplate(
        JustDanceUbiArtFileSystem fileSystem,
        string templatePath,
        ILogger logger,
        [NotNullWhen(true)] out ActorTemplate? actorTemplate)
    {
        actorTemplate = null;
        if (!fileSystem.GetFilePath(templatePath, out CookedFile? templateFile))
            return false;

        try
        {
            using Stream stream = fileSystem.GetFileStream(templateFile);
            actorTemplate = fileSystem.VersionProfile.Serializer != null
                ? fileSystem.VersionProfile.Serializer.Deserialize<ActorTemplate>(stream)
                : JsonSerializer.Deserialize<ActorTemplate>(
                    new StreamReader(stream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'));
            return actorTemplate != null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentOutOfRangeException)
        {
            logger.LogDebug(ex, "Could not read mashup generic tape actor template '{TemplatePath}'.", templatePath);
            return false;
        }
    }

    private static IReadOnlyList<string> ResolveColorTapeLauncherSequence(
        JustDanceUbiArtFileSystem fileSystem,
        IReadOnlyList<MashupColorTapeEntry> colorTapeEntries,
        ILogger logger)
    {
        if (colorTapeEntries.Count == 0)
            return [];

        IReadOnlyDictionary<string, string> tapeNameByLabel = BuildColorTapeNameByLabel(colorTapeEntries);
        foreach (string tapePath in EnumerateMainSequenceTapePaths(fileSystem))
        {
            IReadOnlyList<TapeClip> clips = CinematicTapeClipReader.ReadLocalTapeClips(fileSystem, tapePath, logger);
            foreach (TapeClip clip in clips)
            {
                CinematicTapeLauncherClip? launcher = clip.TapeLauncher;
                if (launcher == null ||
                    launcher.Action != 0 ||
                    launcher.TapeChoice != 1 ||
                    !TargetsGenericTapes(clip))
                {
                    continue;
                }

                List<string> resolvedTapeNames = [];
                foreach (string label in launcher.EffectiveTapeLabels)
                {
                    if (TryResolveColorTapeName(label, tapeNameByLabel, out string? tapeName))
                        resolvedTapeNames.Add(tapeName);
                }

                if (resolvedTapeNames.Count > 0)
                    return resolvedTapeNames;
            }
        }

        return [];
    }

    private static IEnumerable<string> EnumerateMainSequenceTapePaths(JustDanceUbiArtFileSystem fileSystem)
    {
        string cinematicsFolder = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics");
        yield return Path.Combine(cinematicsFolder, "mu_mainsequence.tape");
        yield return Path.Combine(cinematicsFolder, "_mashup_mainsequence.tape");
    }

    private static IReadOnlyDictionary<string, string> BuildColorTapeNameByLabel(
        IReadOnlyList<MashupColorTapeEntry> colorTapeEntries)
    {
        Dictionary<string, string> tapeNameByLabel = new(StringComparer.OrdinalIgnoreCase);
        foreach (MashupColorTapeEntry entry in colorTapeEntries)
        {
            AddColorTapeLabel(tapeNameByLabel, entry.Label, entry.TapeName);
            AddColorTapeLabel(tapeNameByLabel, Path.GetFileNameWithoutExtension(entry.TapeName), entry.TapeName);
            AddColorTapeLabel(tapeNameByLabel, entry.TapeName, entry.TapeName);
        }

        return tapeNameByLabel;
    }

    private static void AddColorTapeLabel(Dictionary<string, string> tapeNameByLabel, string label, string tapeName)
    {
        if (!string.IsNullOrWhiteSpace(label) && !tapeNameByLabel.ContainsKey(label))
            tapeNameByLabel.Add(label, tapeName);
    }

    private static bool TryResolveColorTapeName(
        string label,
        IReadOnlyDictionary<string, string> tapeNameByLabel,
        [NotNullWhen(true)] out string? tapeName)
    {
        tapeName = null;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (tapeNameByLabel.TryGetValue(label, out tapeName))
            return true;

        string candidate = label.EndsWith(".tape", StringComparison.OrdinalIgnoreCase)
            ? GetTapeName(label)
            : $"{label}.tape";
        if (!IsColorTapeName(candidate))
            return false;

        tapeName = candidate;
        return true;
    }

    private static bool TargetsGenericTapes(TapeClip clip) =>
        clip.Targets.Count == 0 ||
        clip.Targets.Any(target => target.Key.EndsWith("mu_generic_tapes", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildColorTapeSequenceFromRack(
        IReadOnlyList<MashupColorTapeEntry> colorTapeEntries) =>
        [
            .. colorTapeEntries
                .Select(entry => entry.TapeName)
                .Where(IsColorTapeName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

    private static string ResolveInitialColorTape(IReadOnlyList<MashupColorTapeEntry> colorTapeEntries)
    {
        MashupColorTapeEntry? authoredGreen = colorTapeEntries.FirstOrDefault(
            entry => string.Equals(entry.TapeName, FallbackInitialColorTape, StringComparison.OrdinalIgnoreCase));
        return authoredGreen?.TapeName ?? FallbackInitialColorTape;
    }

    private static bool IsColorTapeName(string tapeName) =>
        tapeName.StartsWith("color_", StringComparison.OrdinalIgnoreCase) &&
        tapeName.EndsWith(".tape", StringComparison.OrdinalIgnoreCase);

    private static bool IsUvScrollTapeName(string tapeName) =>
        tapeName.StartsWith(UvTapePrefix, StringComparison.OrdinalIgnoreCase) &&
        tapeName.EndsWith(".tape", StringComparison.OrdinalIgnoreCase);

    private sealed record MashupColorTapeEntry(string Label, string Path)
    {
        public string TapeName { get; } = GetTapeName(Path);
    }
}
