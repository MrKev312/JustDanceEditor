using JustDanceEditor.Logging;
using System.Globalization;

namespace JustDanceEditor.Converter.Files;

public class OutputFolders
{
    public OutputFolders(FileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
        CacheNumber = InitializeCacheNumber();
    }

    readonly FileSystem fileSystem;

    public uint CacheNumber { get; set; } = 123;

    public string OutputFolder => Path.Combine(fileSystem.ConversionRequest.OutputPath, fileSystem.SongName);

    public string PreviewFolder => Path.Combine(OutputFolder, $"SD_Cache.0000", "MapBaseCache", fileSystem.ConversionRequest.SongGUID);
    public string CoverFolder => Path.Combine(PreviewFolder, "Cover");
    public string PreviewAudioFolder => Path.Combine(PreviewFolder, "AudioPreview_opus");
    public string PreviewVideoFolder => Path.Combine(PreviewFolder, "VideoPreview_MID_vp9_webm");


    public string MapFolder => Path.Combine(OutputFolder, $"SD_Cache.{CacheNumber:X4}", fileSystem.ConversionRequest.SongGUID);
    public string AudioFolder => Path.Combine(MapFolder, "Audio");
    public string CoachesLargeFolder => Path.Combine(MapFolder, "CoachesLarge");
    public string CoachesSmallFolder => Path.Combine(MapFolder, "CoachesSmall");
    public string MapPackageFolder => Path.Combine(MapFolder, "MapPackage");
    public string SongTitleLogoFolder => Path.Combine(MapFolder, "SongTitleLogo");
    public string VideoFolder => Path.Combine(MapFolder, "Video_HIGH_vp9_webm");

    public string CachingStatusPath => Path.Combine(PreviewFolder, "CachingStatus.json");
    public string CachePath => Path.Combine(OutputFolder, "Cache.json");

    private uint InitializeCacheNumber()
    {
        string cachingStatusPath = Path.Combine(fileSystem.ConversionRequest.OutputPath, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");

        // If the cache number is provided, use it
        if (fileSystem.ConversionRequest.CacheNumber != null)
            return (uint)fileSystem.ConversionRequest.CacheNumber;

        // If we're not in a real cache folder, just use 123
        else if (!File.Exists(cachingStatusPath))
        {
            Logger.Log("Setting cache number to 123", LogLevel.Important);
            return 123;
        }

        // We're in a real cache folder, let's find the max cache number
        // Get all the directories in the output path formatted as SD_Cache.xxxx
        string[] directories = Directory.GetDirectories(fileSystem.ConversionRequest.OutputPath);

        uint maxCacheNumber = 1;
        uint CacheNumber;

        foreach (string directory in directories)
        {
            // If the directory is SD_Cache.0000, skip it
            if (Path.GetFileName(directory).Equals("SD_Cache.0000", StringComparison.CurrentCultureIgnoreCase))
                continue;

            if (directory.Contains("SD_Cache.") && uint.TryParse(directory.Split('.').Last(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint cacheNumber))
                maxCacheNumber = Math.Max(maxCacheNumber, cacheNumber);
        }

        // Create the new cache folder
        Directory.CreateDirectory(Path.Combine(fileSystem.ConversionRequest.OutputPath, $"SD_Cache.{maxCacheNumber:X4}"));

        // If the folder of the max cache number is over 2.8 GB, we'll start a new one
        long cacheSize = new DirectoryInfo(Path.Combine(fileSystem.ConversionRequest.OutputPath, $"SD_Cache.{maxCacheNumber:X4}")).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

        CacheNumber = cacheSize > 2.8f * 1024 * 1024 * 1024
            ? maxCacheNumber + 1
            : maxCacheNumber;

        Logger.Log($"Setting cache number to {CacheNumber}", LogLevel.Important);
        return CacheNumber;
    }
}
