﻿using System.Text.Json;

using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.Converters.Audio;
using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Converters.Bundles;
using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters;

public class ConvertUbiArtToUnity
{
    public JDUbiArtSong SongData { get; private set; } = new();
    public readonly ConversionRequest ConversionRequest;

    /// Folders
    // Main folders
    public string InputFolder => ConversionRequest.InputPath;
    public string OutputFolder => Path.Combine(ConversionRequest.OutputPath, SongData.Name);
    public string TemplateFolder => ConversionRequest.TemplatePath;
    public string TemplateXFolder => Path.Combine(TemplateFolder, "cachex");
    public string Template0Folder => Path.Combine(TemplateFolder, "cache0");
    // Temporary folders
    public string TempMapFolder => Path.Combine(Path.GetTempPath(), "JustDanceEditor", SongData.Name);
    public string TempPictoFolder => Path.Combine(TempMapFolder, "pictos");
    public string TempMenuArtFolder => Path.Combine(TempMapFolder, "menuart");
    public string TempAudioFolder => Path.Combine(TempMapFolder, "audio");
    public string PlatformType { get; private set; } = "NX";
    // Specific map folders
    public string CacheFolder => Path.Combine(InputFolder, "cache", "itf_cooked", PlatformType, "world", "maps", SongData.Name);
    public string MapsFolder => Path.Combine(InputFolder, "world", "maps");
    public string TimelineFolder => Path.Combine(CacheFolder, "timeline");
    public string MovesFolder => Path.Combine(MapsFolder, SongData.Name, "timeline", "moves", "wiiu");
    public string PictosFolder => Path.Combine(TimelineFolder, "pictos");
    public string MenuArtFolder => SongData.EngineVersion == JDVersion.JDUnlimited ?
        Path.Combine(InputFolder, "menuart") :
        Path.Combine(CacheFolder, "menuart", "textures");

    public ConvertUbiArtToUnity(ConversionRequest conversionRequest)
    {
        ConversionRequest = conversionRequest;
    }

    public void Convert()
    {
        Console.WriteLine("Started conversion");
        Stopwatch stopwatch = new();
        stopwatch.Start();

        // Validate the template folder
        ValidateTemplateFolder();

        // Validate the request
        ValidateRequest();

        // Load the song data
        LoadSongData();

        // Create the folders
        CreateTempFolders();

        // Convert the files
        ConversionTasks();

        stopwatch.Stop();
        Console.WriteLine($"Conversion finished in {stopwatch.ElapsedMilliseconds}ms");

        return;
    }

    static void ValidateTemplateFolder()
    {
        string[] folders = [
            "./template/cache0/Cover",
            "./template/cachex/MapPackage",
            "./template/cachex/CoachesLarge",
            "./template/cachex/CoachesSmall"
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
        Console.WriteLine("Loading song info...");

        // Set the song name to bootstrap the process
        SongData = new()
        {
            Name = Path.GetFileName(Directory.GetDirectories(Path.Combine(ConversionRequest.InputPath, "world", "maps"))[0])!
        };

        Console.WriteLine($"Song name: {SongData.Name}");

        string path = Path.Combine(InputFolder, "cache", "itf_cooked");
        PlatformType = Path.GetFileName(Directory.GetDirectories(path).First())!;

        Console.Write($"Platform: {PlatformType}");
        Console.WriteLine(!PlatformType.Equals("nx", StringComparison.CurrentCultureIgnoreCase) ?
            " which is not officially supported. The conversion might not work as expected." :
            "");

        Console.WriteLine("Loading KTape");
        SongData.KTape = JsonSerializer.Deserialize<KaraokeTape>(File.ReadAllText(Path.Combine(TimelineFolder, $"{SongData.Name}_tml_karaoke.ktape.ckd")).Replace("\0", ""))!;
        Console.WriteLine("Loading DTape");
        SongData.DTape = JsonSerializer.Deserialize<DanceTape>(File.ReadAllText(Path.Combine(TimelineFolder, $"{SongData.Name}_tml_dance.dtape.ckd")).Replace("\0", ""))!;
        Console.WriteLine("Loading MTrack");
        SongData.MTrack = JsonSerializer.Deserialize<MusicTrack>(File.ReadAllText(Path.Combine(CacheFolder, "audio", $"{SongData.Name}_musictrack.tpl.ckd")).Replace("\0", ""))!;
        Console.WriteLine("Loading MainSequence");
        SongData.MainSequence = JsonSerializer.Deserialize<MainSequence>(File.ReadAllText(Path.Combine(CacheFolder, "cinematics", $"{SongData.Name}_mainsequence.tape.ckd")).Replace("\0", ""))!;
        Console.WriteLine("Loading SongDesc");
        SongData.SongDesc = JsonSerializer.Deserialize<SongDesc>(File.ReadAllText(Path.Combine(CacheFolder, "songdesc.tpl.ckd")).Replace("\0", ""))!;

        // Get the JD version
        SongData.EngineVersion = !Directory.Exists(MenuArtFolder) ?
            JDVersion.JDUnlimited :
            (JDVersion)SongData.SongDesc.COMPONENTS[0].JDVersion;

        SongData.JDVersion = (JDVersion)SongData.SongDesc.COMPONENTS[0].OriginalJDVersion;

        Console.WriteLine($"Loaded versions, engine: {SongData.EngineVersion}, original version: {SongData.JDVersion}");

        return;
    }

    void CreateTempFolders()
    {
        // Create the folders
        Console.WriteLine("Creating the temp folders");
        Directory.CreateDirectory(TempMapFolder);
        Directory.CreateDirectory(TempPictoFolder);
        Directory.CreateDirectory(TempMenuArtFolder);
        Directory.CreateDirectory(TempAudioFolder);
    }

    private void ConversionTasks()
    {

        // Wait for all tasks to finish
        // Start converting
        Task[] tasks =
        [
            Task.Run(() => MapPackageBundleGenerator.GenerateMapPackage(this)),
            // Convert the audio files in /cache/itf_cooked/nx/world/maps/{mapName}/audio
			Task.Run(() => AudioConverter.ConvertAudio(this)),
            // Convert the menu art in /cache/itf_cooked/nx/world/maps/{mapName}/menuart/textures
			Task.Run(() => MenuArtConverter.ConvertMenuArt(this)).ContinueWith(_ =>{
                // Generate both coaches files
			    Task.Run(() => CoachesLargeBundleGenerator.GenerateCoachesLarge(this));
                Task.Run(() => CoachesSmallBundleGenerator.GenerateCoachesSmall(this));
                // Generate the cover
			    Task.Run(() => CoverBundleGenerator.GenerateCover(this));
            }),
            
            // Generate the song title logo
            Task.Run(() => SongTitleBundleGenerator.GenerateSongTitleLogo(this))
        ];

        // Wait for all tasks to finish
        Task.WaitAll(tasks);
    }
}
