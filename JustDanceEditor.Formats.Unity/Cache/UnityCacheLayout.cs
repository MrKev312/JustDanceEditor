using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.Unity.Cache;

public static partial class UnityCacheLayout
{
    private const int DefaultMaxSearchDepth = 5;
    private static readonly Regex CacheFolderPattern = CacheFolderRegex();

    public static string GetCacheFolderName(uint cacheNumber) => $"SD_Cache.{cacheNumber:X4}";

    public static string? FindCacheRoot(string selectedPath, int maxSearchDepth = DefaultMaxSearchDepth)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return null;

        string? current = Path.GetFullPath(selectedPath);
        for (int depth = 0; depth <= maxSearchDepth && !string.IsNullOrWhiteSpace(current); depth++)
        {
            if (Directory.Exists(current) && ContainsCacheFolder(current))
                return current;

            if (IsCacheFolderPath(current))
                return Directory.GetParent(current)?.FullName;

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    public static async ValueTask<string> ResolveOrCreateAsync(
        string selectedPath,
        UnityConversionRequest request,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (FindCacheRoot(selectedPath) is { } cacheRoot)
        {
            EnsureCacheStructure(cacheRoot, logger);
            return cacheRoot;
        }

        bool shouldCreate;
        if (request.GenerateCacheIfMissing.HasValue)
            shouldCreate = request.GenerateCacheIfMissing.Value;
        else
            shouldCreate = await AskCreateMissingCacheAsync(selectedPath, request, cancellationToken).ConfigureAwait(false);

        if (!shouldCreate)
            throw new InvalidOperationException("Unity cache export was cancelled because no SD_Cache folder was found.");

        Directory.CreateDirectory(selectedPath);
        EnsureCacheStructure(selectedPath, logger);
        return Path.GetFullPath(selectedPath);
    }

    public static void EnsureCacheStructure(string cacheRoot, ILogger? logger = null)
    {
        Directory.CreateDirectory(cacheRoot);

        string cachePath = Path.Combine(cacheRoot, GetCacheFolderName(0));
        string addressablesPath = Path.Combine(cachePath, "Addressables");
        string mapBaseCachePath = Path.Combine(cachePath, "MapBaseCache");

        Directory.CreateDirectory(addressablesPath);
        Directory.CreateDirectory(mapBaseCachePath);

        WriteTextIfMissing(
            Path.Combine(addressablesPath, "json.cache"),
            JDSongJSONBuilder.AddressablesJson(),
            logger);

        WriteTextIfMissing(
            Path.Combine(mapBaseCachePath, "json.cache"),
            JDSongJSONBuilder.MapBaseCacheJson(),
            logger);

        WriteTextIfMissing(
            Path.Combine(mapBaseCachePath, "CachingStatus.json"),
            JsonSerializer.Serialize(new JDCacheJSON(), UnityCacheJson.Options),
            logger);
    }

    private static async ValueTask<bool> AskCreateMissingCacheAsync(
        string selectedPath,
        UnityConversionRequest request,
        CancellationToken cancellationToken)
    {
        IConversionInteraction interaction = request.Interaction
            ?? throw new InvalidOperationException($"No SD_Cache folders were found near '{selectedPath}'. Pass --answer {UnityPromptIds.GenerateCacheIfMissing}=true to generate a new cache setup in headless mode.");

        bool folderExists = Directory.Exists(selectedPath);
        bool isNonEmpty = folderExists && Directory.EnumerateFileSystemEntries(selectedPath).Any();
        string label = isNonEmpty
            ? $"No SD_Cache folders were found near '{selectedPath}'. The selected folder is not empty, so this is likely the wrong output folder. Generate a new SD cache setup in this folder?"
            : $"No SD_Cache folders were found near '{selectedPath}'. Generate a new SD cache setup in this folder?";

        PromptAnswerSet answers = await interaction.AskAsync(
            new ConversionPromptSet(
                "unity.cache.missing",
                "Unity cache not found",
                [
                    new ConversionPrompt(
                        UnityPromptIds.GenerateCacheIfMissing,
                        ConversionPromptKind.Boolean,
                        label,
                        Required: true)
                ]),
            cancellationToken).ConfigureAwait(false);

        return answers.TryGetString(UnityPromptIds.GenerateCacheIfMissing, out string? value)
               && bool.TryParse(value, out bool parsed)
               && parsed;
    }

    private static bool ContainsCacheFolder(string path) =>
        Directory.EnumerateDirectories(path).Any(folder => IsCacheFolderName(Path.GetFileName(folder)));

    private static bool IsCacheFolderPath(string path) =>
        IsCacheFolderName(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

    private static bool IsCacheFolderName(string folderName) => CacheFolderPattern.IsMatch(folderName);

    private static void WriteTextIfMissing(string path, string contents, ILogger? logger)
    {
        if (File.Exists(path))
            return;

        File.WriteAllText(path, contents);
        logger?.LogInformation("Created {Path}", path);
    }

    [GeneratedRegex(@"^SD_Cache\.[0-9A-Fa-f]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex CacheFolderRegex();
}
