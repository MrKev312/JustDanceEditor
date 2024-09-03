using System.Diagnostics;
using System.Text.Json;

using JustDanceEditor.Converter.Files;

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


namespace JustDanceEditor.Converter.Converters;

public class ConvertUbiArtToUnity(ConversionRequest conversionRequest)
{
    public JDUbiArtSong SongData { get; private set; } = new();
    public ConversionRequest ConversionRequest = conversionRequest;
    public FileSystem FileSystem { get; private set; } = new(conversionRequest);

    public string SongID => ConversionRequest.SongGUID;

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

        // Convert the files
        ConversionTasks();

        // Generate the cache
        GenerateCache();

        // Merge the cache
        bool canClearTemp = MergeCacheFiles();

#if RELEASE
        if (canClearTemp)
            FileSystem.TempFolders.Delete();
#endif

        stopwatch.Stop();
        Logger.Log($"Conversion finished in {stopwatch.ElapsedMilliseconds}ms");

        return;
    }

    bool MergeCacheFiles()
    {
        if (File.Exists(FileSystem.OutputFolders.CachePath) &&
            File.Exists(FileSystem.OutputFolders.CachingStatusPath))
            return CacheJsonGenerator.MergeCaches(this);

        return true;
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

    void LoadSongData()
    {
        // Load in the song data
        Logger.Log("Loading song info...");

        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntBoolConverter());

        List<IClip> clips = [];

        // Let's start with the songdesc
        Logger.Log("Loading SongDesc");
        string relativePath = Path.Combine("world", "maps", SongID, "songdesc.tpl.ckd");
        string path = FileSystem.GetFilePath(relativePath);
        SongData.SongDesc = JsonSerializer.Deserialize<SongDesc>(FileSystem.ReadWithoutNulls(path), options)!;

        // Get the map name
        SongData.Name = SongData.SongDesc.COMPONENTS[0].MapName;

        // Load the JD version
        Logger.Log("Loading JDVersion");
        SongData.EngineVersion = (JDVersion)SongData.SongDesc.COMPONENTS[0].JDVersion;
        SongData.JDVersion = (JDVersion)SongData.SongDesc.COMPONENTS[0].OriginalJDVersion;
        Logger.Log($"Loaded versions, engine: {SongData.EngineVersion}, original version: {SongData.JDVersion}");

        // Load the music track
        Logger.Log("Loading MusicTrack");
        relativePath = Path.Combine("audio", $"{SongData.Name}_musictrack.tpl.ckd");
        path = FileSystem.GetFilePath(relativePath);
        SongData.MusicTrack = JsonSerializer.Deserialize<MusicTrack>(FileSystem.ReadWithoutNulls(path), options)!;

        // Load the clips
        Logger.Log("Loading DanceTape");
        relativePath = Path.Combine("timeline", SongID, $"{SongData.Name}_tml_dance.dtape.ckd");
        path = FileSystem.GetFilePath(relativePath);
        ClipTape DanceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNulls(path), options)!;
        clips.AddRange(DanceTape.Clips);

        Logger.Log("Loading MainSequence");
        relativePath = Path.Combine("cinematics", $"{SongData.Name}_mainsequence.tape.ckd");
        path = FileSystem.GetFilePath(relativePath);
        ClipTape MainSequenceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNulls(path), options)!;
        clips.AddRange(MainSequenceTape.Clips);

        // Some maps don't have KaraokeTape
        if (FileSystem.GetFilePath(Path.Combine("timeline", $"{SongID}_tml_karaoke.ktape.ckd"), out string? karaokePath))
        {
            Logger.Log("Loading KaraokeTape");
            ClipTape KaraokeTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNulls(karaokePath), options)!;
            clips.AddRange(KaraokeTape.Clips);
        }
        else
            Logger.Log("KaraokeTape not found");

        SongData.Clips = [.. clips];

        return;
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
