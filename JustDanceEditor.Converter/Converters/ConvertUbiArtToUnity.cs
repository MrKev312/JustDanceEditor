using System.Diagnostics;
using System.Text.Json;

using JustDanceEditor.Converter.Converters.Audio;
using JustDanceEditor.Converter.Converters.Bundles;
using JustDanceEditor.Converter.Converters.Cache;
using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Converters.Video;

using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

using JustDanceEditor.Converter.Helpers;

using Xabe.FFmpeg.Downloader;
using JustDanceEditor.Logging;
using System.Globalization;

namespace JustDanceEditor.Converter.Converters;

public class ConvertUbiArtToUnity(ConversionRequest conversionRequest)
{
    public JDUbiArtSong SongData { get; private set; } = new();
    public readonly ConversionRequest ConversionRequest = conversionRequest;

    /// Folders
    // Main folders
    public string InputFolder => ConversionRequest.InputPath;
    public string InputMenuArtFolder => Path.Combine(WorldFolder, "menuart");
    public string InputMediaFolder => Path.Combine(WorldFolder, "media");
    public string OutputFolder => Path.Combine(ConversionRequest.OutputPath, SongData.Name);
    public string Output0Folder => Path.Combine(OutputFolder, $"SD_Cache.0000", "MapBaseCache", SongID);
    public string OutputXFolder => Path.Combine(OutputFolder, $"SD_Cache.{CacheNumber:X4}", SongID);
    public string TemplateFolder => ConversionRequest.TemplatePath;
    public string SongID => ConversionRequest.SongID;
    public uint CacheNumber => ConversionRequest.CacheNumber ?? 123;
    // Temporary folders
    public string TempMapFolder => Path.Combine(Path.GetTempPath(), "JustDanceEditor", SongData.Name);
    public string TempPictoFolder => Path.Combine(TempMapFolder, "pictos");
    public string TempPictoAtlasFolder => Path.Combine(TempPictoFolder, "Atlas");
    public string TempMenuArtFolder => Path.Combine(TempMapFolder, "menuart");
    public string TempAudioFolder => Path.Combine(TempMapFolder, "audio");
    public string TempVideoFolder => Path.Combine(TempMapFolder, "video");
    public string PlatformType { get; private set; } = "NX";
    // Specific map folders
    public string CacheFolder => Path.Combine(InputFolder, "cache", "itf_cooked", PlatformType, "world", "maps", SongData.Name);
    public string WorldFolder => Path.Combine(InputFolder, "world", "maps", SongData.Name);
    public string TimelineFolder => Path.Combine(CacheFolder, "timeline");
    public string MovesFolder => Path.Combine(WorldFolder, "timeline", "moves", "wiiu");
    public string PictosFolder => Path.Combine(TimelineFolder, "pictos");
    public string MenuArtFolder => SongData.EngineVersion == JDVersion.JDUnlimited ?
        InputMenuArtFolder :
        Path.Combine(CacheFolder, "menuart", "textures");

    public void Convert()
    {
        Logger.Log("Started conversion");
        Stopwatch stopwatch = new();
        stopwatch.Start();

        // Validate the template folder
        ValidateTemplateFolder();

        // Validate the request
        ValidateRequest();

        // Load the song data
        LoadSongData();

        // Given that we can load the song data, we can determine the cache number
        DetermineCacheNumber();

        // Create the folders
        CreateTempFolders();

        // Convert the files
        ConversionTasks();

        // Generate the cache
        GenerateCache();

        // Merge the cache
        MergeCacheFiles();

        stopwatch.Stop();
        Logger.Log($"Conversion finished in {stopwatch.ElapsedMilliseconds}ms");

        return;
    }

    void MergeCacheFiles()
    {
        if (File.Exists(Path.Combine(OutputFolder, "cachingStatus.json")) &&
            File.Exists(Path.Combine(ConversionRequest.OutputPath, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json")))
            CacheJsonGenerator.MergeCaches(this);
    }

    void GenerateCache()
    {
        CacheJsonGenerator.GenerateCacheJson(this);
    }

    static void ValidateTemplateFolder()
    {
        string[] folders = [
            "./template/Cover",
            "./template/MapPackage",
            "./template/CoachesLarge",
            "./template/CoachesSmall"
        ];

        // If any of the folders don't exist, throw an exception
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException($"The folder {folder} is missing. Please put a template file in the folder.");
            }
        }

        // If any of the folders is empty, throw an exception
        foreach (string folder in folders)
        {
            if (Directory.GetFiles(folder).Length == 0)
            {
                throw new FileNotFoundException($"The folder {folder} is empty. Please put a template file in the folder.");
            }
        }
    }

    void ValidateRequest()
    {
        ArgumentNullException.ThrowIfNull(ConversionRequest);

        // Validate the input path
        if (string.IsNullOrWhiteSpace(ConversionRequest.InputPath) || !Directory.Exists(ConversionRequest.InputPath))
            throw new FileNotFoundException("Input folder not found");

        // Validate the output path by checking if it's a valid URI
        if (string.IsNullOrWhiteSpace(ConversionRequest.OutputPath))
            throw new ArgumentException("Output path is not valid URI");

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(ConversionRequest.OutputPath);
    }

    void DetermineCacheNumber()
    {
        string cachingStatusPath = Path.Combine(ConversionRequest.OutputPath, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");

        // If the cache number is provided, use it
        if (ConversionRequest.CacheNumber != null)
            return;
        // If we're not in a real cache folder, just use 123
        else if (!File.Exists(cachingStatusPath))
        {
            ConversionRequest.CacheNumber = 123;
            Logger.Log("Setting cache number to 123", LogLevel.Important);
            return;
        }

        // We're in a real cache folder, let's find the max cache number
        // Get all the directories in the output path formatted as SD_Cache.xxxx
        string[] directories = Directory.GetDirectories(ConversionRequest.OutputPath);

        uint maxCacheNumber = 1;

        foreach (string directory in directories)
        {
            // If the directory is SD_Cache.0000, skip it
            if (Path.GetFileName(directory).Equals("SD_Cache.0000", StringComparison.CurrentCultureIgnoreCase))
                continue;

            if (directory.Contains("SD_Cache.") && uint.TryParse(directory.Split('.').Last(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint cacheNumber))
                maxCacheNumber = Math.Max(maxCacheNumber, cacheNumber);
        }

        // Create the new cache folder
        Directory.CreateDirectory(Path.Combine(ConversionRequest.OutputPath, $"SD_Cache.{maxCacheNumber:X4}"));

        // If the folder of the max cache number is over 3 GB, we'll start a new one
        long cacheSize = new DirectoryInfo(Path.Combine(ConversionRequest.OutputPath, $"SD_Cache.{maxCacheNumber:X4}")).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

        ConversionRequest.CacheNumber = cacheSize > 3u * 1024 * 1024 * 1024 
            ? maxCacheNumber + 1 
            : maxCacheNumber;

        Logger.Log($"Setting cache number to {CacheNumber}", LogLevel.Important);
    }

    void LoadSongData()
    {
        // Load in the song data
        Logger.Log("Loading song info...");

        // Set the song name to bootstrap the process
        string mapName = ConversionRequest.SongName
            ?? Path.GetFileName(Directory.GetDirectories(Path.Combine(ConversionRequest.InputPath, "world", "maps"))[0]);

        SongData = new()
        {
            Name = mapName
        };

        Logger.Log($"Song name: {SongData.Name}");

        string path = Path.Combine(InputFolder, "cache", "itf_cooked");
        PlatformType = Path.GetFileName(Directory.GetDirectories(path).First())!;

        string message = $"Platform: {PlatformType}";
        if (!PlatformType.Equals("nx", StringComparison.CurrentCultureIgnoreCase))
            message += " which is not officially supported. The conversion might not work as expected.";

        Logger.Log(message);

        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntBoolConverter());

        List<IClip> clips = [];

        Logger.Log("Loading MusicTrack");
        SongData.MusicTrack = JsonSerializer.Deserialize<MusicTrack>(File.ReadAllText(Path.Combine(CacheFolder, "audio", $"{SongData.Name}_musictrack.tpl.ckd")).Replace("\0", ""), options)!;

        // Loading clips
        Logger.Log("Loading DanceTape");
        ClipTape DanceTape = JsonSerializer.Deserialize<ClipTape>(File.ReadAllText(Path.Combine(TimelineFolder, $"{SongData.Name}_tml_dance.dtape.ckd")).Replace("\0", ""), options)!;
        clips.AddRange(DanceTape.Clips);
        Logger.Log("Loading MainSequence");
        ClipTape MainSequenceTape = JsonSerializer.Deserialize<ClipTape>(File.ReadAllText(Path.Combine(CacheFolder, "cinematics", $"{SongData.Name}_mainsequence.tape.ckd")).Replace("\0", ""), options)!;
        clips.AddRange(MainSequenceTape.Clips);
        // Some maps don't have KaraokeTape
        if (File.Exists(Path.Combine(TimelineFolder, $"{SongData.Name}_tml_karaoke.ktape.ckd")))
        {
            Logger.Log("Loading KaraokeTape");
            ClipTape KaraokeTape = JsonSerializer.Deserialize<ClipTape>(File.ReadAllText(Path.Combine(TimelineFolder, $"{SongData.Name}_tml_karaoke.ktape.ckd")).Replace("\0", ""), options)!;
            clips.AddRange(KaraokeTape.Clips);
        }
        else
            Logger.Log("KaraokeTape not found");
        SongData.Clips = [.. clips];

        Logger.Log("Loading SongDesc");
        SongDesc? songDesc = null;
        List<string> songDescLocs = [
            Path.Combine(InputFolder, "..", "patch_nx", "cache", "itf_cooked", PlatformType, "world", "maps", SongData.Name, "songdesc.tpl.ckd"),
            Path.Combine(InputFolder, "..", "bundle_nx", "cache", "itf_cooked", PlatformType, "world", "maps", SongData.Name, "songdesc.tpl.ckd"),
            Path.Combine(CacheFolder, "songdesc.tpl.ckd")
            ];

        foreach (string loc in songDescLocs)
        {
            if (File.Exists(loc))
            {
                songDesc = JsonSerializer.Deserialize<SongDesc>(File.ReadAllText(loc).Replace("\0", ""), options);
                break;
            }
        }

        if (songDesc == null)
            throw new FileNotFoundException("SongDesc not found");

        SongData.SongDesc = songDesc;
        SongData.Name = SongData.SongDesc.COMPONENTS[0].MapName;

        // Get the JD version
        Logger.Log("Loading JDVersion");
        SongData.EngineVersion = !Directory.Exists(MenuArtFolder) ?
            JDVersion.JDUnlimited :
            (JDVersion)SongData.SongDesc.COMPONENTS[0].JDVersion;

        SongData.JDVersion = (JDVersion)(ConversionRequest.JDVersion ?? SongData.SongDesc.COMPONENTS[0].OriginalJDVersion);

        Logger.Log($"Loaded versions, engine: {SongData.EngineVersion}, original version: {SongData.JDVersion}");

        return;
    }

    void CreateTempFolders()
    {
        // Delete the old temp folder
        if (Directory.Exists(TempMapFolder))
        {
            Logger.Log("Deleting the old temp folder", LogLevel.Debug);
            Directory.Delete(TempMapFolder, true);
        }

        // Create the folders
        Logger.Log("Creating temp folders", LogLevel.Debug);
        Directory.CreateDirectory(TempMapFolder);
        Directory.CreateDirectory(TempPictoFolder);
        Directory.CreateDirectory(TempPictoAtlasFolder);
        Directory.CreateDirectory(TempMenuArtFolder);
        Directory.CreateDirectory(TempAudioFolder);
        Directory.CreateDirectory(TempVideoFolder);
    }

    void ConversionTasks()
    {
        Logger.Log("Starting conversion tasks", LogLevel.Debug);

        // List of tasks to await
        Task[] tasks =
        [
            GenerateMapPackageAsync(),
            ConvertMediaAsync(),
            ConvertMenuArtAndGenerateAssetsAsync(),
            ConvertSongTitleLogoAsync()
        ];

        Task.WaitAll(tasks);

        Logger.Log("Conversion tasks finished", LogLevel.Debug);
    }

    async Task GenerateMapPackageAsync()
    {
        await MapPackageBundleGenerator.GenerateMapPackageAsync(this);
    }

    async Task ConvertMediaAsync()
    {
        // First, download FFmpeg if needed
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

        // Then, convert the audio and video
        await Task.WhenAll(
            AudioConverter.ConvertAudioAsync(this),
            VideoConverter.ConvertVideoAsync(this)
        );
    }

    async Task ConvertMenuArtAndGenerateAssetsAsync()
    {
        // First, convert the menu art
        await MenuArtConverter.ConvertMenuArtAsync(this);

        // Then, generate the assets
        await Task.WhenAll(
            CoachesLargeBundleGenerator.GenerateCoachesLargeAsync(this),
            CoachesSmallBundleGenerator.GenerateCoachesSmallAsync(this),
            CoverBundleGenerator.GenerateCoverAsync(this)
        );
    }

    async Task ConvertSongTitleLogoAsync()
    {
        await SongTitleBundleGenerator.GenerateSongTitleLogoAsync(this);
    }
}
