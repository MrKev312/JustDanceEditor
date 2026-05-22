using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Cache;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Tools;

public sealed class UnityCacheToolProvider(ILogger<UnityCacheToolProvider> logger) : IToolProvider
{
    private const string SpreadCacheToolCode = "cache-spread";

    private readonly ILogger<UnityCacheToolProvider> _logger = logger;

    public string ProviderCode => "unity";
    public string ProviderName => "Unity";
    public int Priority => 40;

    public IReadOnlyCollection<ToolDefinition> GetTools() =>
    [
        new(
            ProviderCode,
            ProviderName,
            SpreadCacheToolCode,
            "Spread NX Cache Folders",
            "Move songs from overflow cache folders back into balanced cache folders for exFAT caches.",
            [
                new ConversionPrompt(
                    ConversionPromptIds.InputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the cache root folder",
                    Required: true,
                    MustExist: true)
            ],
            Priority: 20)
    ];

    public async Task ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (context.Tool.ToolCode)
        {
            case SpreadCacheToolCode:
                await SpreadCacheAsync(
                    context.Answers.GetString(ConversionPromptIds.InputPath),
                    GetBoolean(context.Answers, ConversionPromptIds.Force),
                    context.Interaction,
                    cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unknown Unity tool '{context.Tool.ToolCode}'.");
        }
    }

    private async Task SpreadCacheAsync(string cachePath, bool continueIfThresholdMissing, IConversionInteraction? interaction, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.0000")))
            throw new DirectoryNotFoundException("The specified folder does not appear to be a valid cache. Missing 'SD_Cache.0000'.");

        if (!Directory.Exists(Path.Combine(cachePath, "SD_Cache.002A")))
        {
            _logger.LogWarning("Cache folder 'SD_Cache.002A' was not found. Spreading might not be necessary yet.");
            if (!continueIfThresholdMissing && interaction is not null)
            {
                PromptAnswerSet followUp = await interaction.AskAsync(new ConversionPromptSet(
                    "unity.cache-spread.thresholdMissing",
                    "Cache threshold folder not found",
                    [
                        new ConversionPrompt(
                            ConversionPromptIds.Force,
                            ConversionPromptKind.Boolean,
                            "SD_Cache.002A is missing. Continue anyway?",
                            Required: false,
                            DefaultValue: "false")
                    ]), cancellationToken);

                continueIfThresholdMissing = GetBoolean(followUp, ConversionPromptIds.Force);
            }

            if (!continueIfThresholdMissing)
                throw new InvalidOperationException("Cache folder 'SD_Cache.002A' was not found. Use --force to continue anyway.");
        }

        string cachingStatusJsonPath = Path.Combine(cachePath, "SD_Cache.0000", "MapBaseCache", "CachingStatus.json");
        if (!File.Exists(cachingStatusJsonPath))
            throw new FileNotFoundException("'CachingStatus.json' was not found in the cache.", cachingStatusJsonPath);

        _logger.LogInformation("Analyzing cache structure at {CachePath}", cachePath);

        using FileStream json = File.OpenRead(cachingStatusJsonPath);
        JDCacheJSON cacheJson = JsonSerializer.Deserialize<JDCacheJSON>(json, UnityCacheJson.Options) ?? throw new JsonException("Failed to deserialize CachingStatus.json.");

        string[] folders = Directory.GetDirectories(cachePath);
        PriorityQueue<string, long> cacheOutputFolders = new();
        List<string> cacheInputFolders = [];

        foreach (string folder in folders)
        {
            string folderName = Path.GetFileName(folder);
            if (folderName == "SD_Cache.0000" || !folderName.StartsWith("SD_Cache.", StringComparison.OrdinalIgnoreCase))
                continue;

            if (cacheOutputFolders.Count < 0x29)
                cacheOutputFolders.Enqueue(folderName, GetFolderSize(folder));
            else
                cacheInputFolders.Add(folderName);
        }

        if (cacheInputFolders.Count == 0)
        {
            _logger.LogInformation("No folders were found beyond the initial cache folder set. Spreading is not needed.");
            return;
        }

        foreach (string folder in cacheInputFolders)
        {
            string fullFolder = Path.Combine(cachePath, folder);
            _logger.LogInformation("Spreading songs from {Folder}", folder);

            foreach (string songFolder in Directory.GetDirectories(fullFolder))
            {
                Guid songFolderName = Guid.Parse(Path.GetFileName(songFolder));
                if (!cacheJson.MapsDict.TryGetValue(songFolderName, out JDCacheSong? song))
                    throw new KeyNotFoundException($"Song '{songFolderName}' was not found in CachingStatus.json.");

                if (!cacheOutputFolders.TryDequeue(out string? outputFolder, out long outputFolderSize))
                    throw new InvalidOperationException("Ran out of target cache folders while spreading the cache.");

                uint folderNumber = uint.Parse(outputFolder[^4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                _logger.LogInformation("Moving song {SongGuid} to {OutputFolder}", songFolderName, outputFolder);

                JDSongJSONBuilder.UpdateSong(song, folderNumber);
                cacheJson.MapsDict[songFolderName] = song;

                long songFolderSize = GetFolderSize(songFolder);
                cacheOutputFolders.Enqueue(outputFolder, outputFolderSize + songFolderSize);

                string fullSongFolderOutput = Path.Combine(cachePath, outputFolder, songFolderName.ToString());
                Directory.Move(songFolder, fullSongFolderOutput);

                string jsonCachePath = Path.Combine(fullSongFolderOutput, "json.cache");
                File.WriteAllText(jsonCachePath, JDSongJSONBuilder.CacheJson(folderNumber, songFolderName));
            }

            Directory.Delete(fullFolder);
            _logger.LogInformation("Deleted empty source folder {Folder}", folder);
        }

        string backupPath = cachingStatusJsonPath + ".bak";
        File.Copy(cachingStatusJsonPath, backupPath, true);
        _logger.LogInformation("Backed up CachingStatus.json to {BackupPath}", backupPath);

        File.WriteAllText(cachingStatusJsonPath, JsonSerializer.Serialize(cacheJson, UnityCacheJson.Options));
    }

    private static long GetFolderSize(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static bool GetBoolean(PromptAnswerSet answers, string id) =>
        answers.TryGetString(id, out string? value) && bool.TryParse(value, out bool parsed) && parsed;
}
