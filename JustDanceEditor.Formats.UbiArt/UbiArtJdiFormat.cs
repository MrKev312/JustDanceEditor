using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtJdiFormat(ISongDataLoader songDataLoader, Func<UbiArtConversionRequest, UbiArtVersionProfile, JustDanceUbiArtFileSystem> fileSystemFactory, IUbiArtEngineDetector engineDetector, IAudioConverter? audioConverter, JDI.Services.ITextureService? textureService, IUbiArtAssetWriter? assetWriter, ILogger<UbiArtJdiFormat> logger, JDI.Services.IFileSystem? io = null) : IJdiFormat
{
    private readonly ISongDataLoader _songDataLoader = songDataLoader;
    private readonly Func<UbiArtConversionRequest, UbiArtVersionProfile, JustDanceUbiArtFileSystem> _fileSystemFactory = fileSystemFactory;
    private readonly IUbiArtEngineDetector _engineDetector = engineDetector ?? throw new ArgumentNullException(nameof(engineDetector));
    private readonly ILogger<UbiArtJdiFormat> _logger = logger;
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    private IAudioConverter AudioConverter { get => field ?? throw new InvalidOperationException("An audio converter is required for UbiArt import operations."); } = audioConverter;

    private JDI.Services.ITextureService TextureService { get => field ?? throw new InvalidOperationException("A texture service is required for UbiArt import operations."); } = textureService;

    private IUbiArtAssetWriter AssetWriter { get => field ?? throw new InvalidOperationException("An asset writer is required for UbiArt export operations."); } = assetWriter;

    public string DisplayName => "UbiArt";
    public bool CanImport => true;
    public bool CanExport => true;

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt import expects a UbiArtConversionRequest.", nameof(request));

        _logger.LogInformation("Starting UbiArt -> JDI conversion from '{InputPath}'", ubiRequest.InputPath);

        UbiArtVersionProfile profile = _engineDetector.Detect(ubiRequest.InputPath);
        _logger.LogInformation("Detected engine container: {Container}, engine version: {Version}", profile.Platform, profile.EngineVersion);

        JustDanceUbiArtFileSystem fileSystem = _fileSystemFactory(ubiRequest, profile);
        fileSystem.Initialize();
        _logger.LogDebug("Initialized layered filesystem for UbiArt import from '{InputPath}'", ubiRequest.InputPath);

        // Resolve selected song (may call into UI via request.SelectSongAsync)
        string chosenSong = await ResolveSongAsync(ubiRequest, fileSystem);
        if (!string.IsNullOrWhiteSpace(chosenSong))
            fileSystem.UpdateSongName(chosenSong);
        _logger.LogDebug("Selected UbiArt song '{SongName}' for import", fileSystem.SongName);

        ValidateUbiArtImport(ubiRequest, fileSystem);
        _logger.LogDebug("Validated UbiArt input for '{SongName}'", fileSystem.SongName);

        if (!string.IsNullOrWhiteSpace(fileSystem.SongName))
            _logger.LogInformation("Song name: {SongName}", fileSystem.SongName);

        string platformName = fileSystem.VersionProfile.Platform.ToString();
        ConversionSupportStatus supportStatus = GetPlatformSupportStatus(fileSystem.VersionProfile.Platform);
        if (supportStatus == ConversionSupportStatus.Stable)
        {
            _logger.LogInformation("Platform: {Platform}", platformName);
        }
        else
        {
            _logger.LogWarning(
                "Platform: {Platform} is {SupportStatus}. The conversion might not work as expected.",
                platformName,
                FormatSupportStatus(supportStatus));
        }

        ConversionContext context;
        try
        {
            context = new(ubiRequest, fileSystem)
            {
                SongData = _songDataLoader.LoadSongData(ubiRequest, fileSystem)
            };
        }
        catch (NotImplementedException ex)
        {
            // If the configured serializer is binary, provide a friendly message until legacy import is implemented.
            if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014 || fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015)
                throw new NotSupportedException("JD2014/2015 binary support is coming soon.", ex);

            if (fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer)
                throw new NotSupportedException("Legacy binary UbiArt import support is coming soon.", ex);

            throw;
        }

        // Manage temporary folders explicitly in the import workflow (caller-managed cleanup)
        string previousMap = fileSystem.SongName;
        context.FileSystem.UpdateSongName(context.SongData.Name);
        // Create new temp folders for this map (if needed)
        context.FileSystem.TempFolders.CreateTempFolders();
        // If the map name changed, request deletion of the previous temp folder
        if (!string.IsNullOrWhiteSpace(previousMap) && !string.Equals(previousMap, context.FileSystem.SongName, StringComparison.Ordinal))
        {
            context.FileSystem.TempFolders.DeleteMap(previousMap);
        }

        context.IntermediatePackage = IntermediatePackageBuilder.FromUbiArt(context);
        string outputFolder = _io.Combine(ubiRequest.OutputPath, context.SongData.Name);
        PrepareOutputDirectory(outputFolder);
        _logger.LogInformation("Materializing UbiArt assets into JDI package at '{OutputFolder}'", outputFolder);

        await IntermediateAssetWriter.PopulateFromUbiArtAsync(context, context.IntermediatePackage, outputFolder, _logger, TextureService, AudioConverter);
        _logger.LogDebug("Writing JDI package metadata to '{OutputFolder}'", outputFolder);
        IntermediatePackageSerializer.WriteToFolder(context.IntermediatePackage, outputFolder);

        _logger.LogInformation("UbiArt -> JDI conversion completed for '{SongName}' at '{OutputFolder}'", context.SongData.Name, outputFolder);

        return new JdiImportResult(
            context.IntermediatePackage,
            "UbiArt",
            outputFolder,
            MaterializedRootIsTemporary: outputFolder.Contains(_io.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            SuggestedOutputFolder: outputFolder);
    }

    public async Task<string> ResolveSongAsync(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem)
    {
        // If already specified, return it
        if (!string.IsNullOrWhiteSpace(fileSystem.SongName))
            return fileSystem.SongName;

        (string SongName, string SongDescPath)[] available = fileSystem.GetAvailableSongs();
        if (available.Length == 0)
            throw new InvalidOperationException("No songs found in the input bundle.");
        if (available.Length == 1)
            return available[0].SongName;

        // Multiple songs: if caller provided a selector, use it
        if (request.SelectSongAsync != null)
        {
            string[] names = [.. available.Select(s => s.SongName)];
            string? selected = await request.SelectSongAsync(names);
            if (string.IsNullOrWhiteSpace(selected))
                throw new OperationCanceledException("Song selection was canceled by the caller.");

            if (!names.Contains(selected, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Selected song is not in the available songs list.", nameof(selected));

            return selected;
        }

        // No selector provided: throw the MultipleSongsFoundException so UI can catch and handle
        throw new MultipleSongsFoundException(available.Select(s => s.SongName));
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt export expects a UbiArtConversionRequest.", nameof(request));

        ArgumentNullException.ThrowIfNull(importResult);
        ArgumentNullException.ThrowIfNull(importResult.Package);

        // Create subfolder with {songname}_{platform} pattern in lowercase
        string songName = importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song";
        string platformName = ubiRequest.ExportPlatform == UbiArtPlatform.Uncooked
            ? "uncooked"
            : ubiRequest.ExportPlatform.GetCookedFolderName();
        string folderName = $"{songName.ToLowerInvariant()}_{platformName}";
        string outputFolder = Path.Combine(ubiRequest.OutputPath, folderName);

        _logger.LogInformation(
            "Starting JDI -> UbiArt conversion for '{SongName}' ({Platform}, {EngineVersion}) into '{OutputFolder}'",
            songName,
            ubiRequest.ExportPlatform,
            ubiRequest.ExportEngineVersion,
            outputFolder);

        // Ensure output directory exists
        _io.CreateDirectory(outputFolder);
        _logger.LogDebug("Prepared UbiArt output directory '{OutputFolder}'", outputFolder);

        // Convert request enums to UbiArt Services enums
        UbiArtPlatform exportPlatform = ubiRequest.ExportPlatform;
        UbiArtEngineVersion exportEngineVersion = ubiRequest.ExportEngineVersion;

        // Create appropriate profile for export
        IUbiArtSerializer serializer = exportPlatform == UbiArtPlatform.Uncooked
            ? new LuaUbiArtSerializer()
            : new JsonUbiArtSerializer();

        UbiArtVersionProfile exportProfile = new(
            exportPlatform,
            exportEngineVersion,
            new UbiArtLayoutResolver(),
            serializer);

        await AssetWriter.ExportAsync(
            importResult.Package,
            importResult.MaterializedRoot,
            outputFolder,
            exportPlatform,
            exportEngineVersion,
            exportProfile.Layout);

        _logger.LogInformation("JDI -> UbiArt conversion completed for '{SongName}' at '{OutputFolder}'", songName, outputFolder);
    }

    private void PrepareOutputDirectory(string targetFolder)
    {
        if (_io.DirectoryExists(targetFolder))
            _io.DeleteDirectory(targetFolder, true);
        _io.CreateDirectory(targetFolder);
    }

    private static ConversionSupportStatus GetPlatformSupportStatus(UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Wii => ConversionSupportStatus.Experimental,
        UbiArtPlatform.X360 => ConversionSupportStatus.Experimental,
        UbiArtPlatform.Durango => ConversionSupportStatus.KnownPartial,
        _ => ConversionSupportStatus.Stable
    };

    private static string FormatSupportStatus(ConversionSupportStatus supportStatus) => supportStatus switch
    {
        ConversionSupportStatus.Experimental => "experimental",
        ConversionSupportStatus.KnownPartial => "partially supported",
        _ => "stable"
    };

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!_io.DirectoryExists(path) && !Path.GetFileName(path).EndsWith(".ipk", StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            UbiArtVersionProfile profile = _engineDetector.Detect(path);
            UbiArtConversionRequest req = new(path, _io.GetTempPath(), null) { Type = profile.Platform == UbiArtPlatform.Uncooked ? CookedType.Uncooked : CookedType.Cooked };
            JustDanceUbiArtFileSystem fs = _fileSystemFactory(req, profile);
            fs.Initialize();

            // For Check, we just need to verify that maps exist with songdesc files
            // Don't try to initialize a specific song - just check if ANY songdesc.tpl exists in the maps folder
            (string SongName, string SongDescPath)[] availableSongs = fs.GetAvailableSongs();
            if (availableSongs.Length == 0)
                return false;

            // Attempt to load song data from the first available song to determine engine version and report platform
            try
            {
                // Update filesystem with first song temporarily for verification
                fs.UpdateSongName(availableSongs[0].SongName);
                SongDesc sd = _songDataLoader.LoadSongDesc(req, fs);
                uint engine = sd.Components[0].JDVersion;
                uint original = sd.Components[0].OriginalJDVersion;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Detected UbiArt platform: {fs.VersionProfile.Platform}, engine version: {engine}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("UbiArt detection: failed to read SongDesc for engine/version: {Message}", ex.Message);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("UbiArt detection failed: {Message}", ex.Message);
            return false;
        }
    }

    private bool ContainsFileRecursive(string root, string fileName, JustDanceUbiArtFileSystem fs)
    {
        // If root points at an IPK file, use the JustDanceUbiArtFileSystem search helpers which are IPK-aware
        if (Path.GetExtension(root).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // fs.GetFilePath understands IPKs and adjacent folders; use it to check for the presence of the file
                return fs.GetFilePath(fileName, out _);
            }
            catch
            {
                return false;
            }
        }

        // Guard against non-existent directories
        if (!_io.DirectoryExists(root))
            return false;

        if (_io.GetFiles(root, fileName).Length > 0)
            return true;

        foreach (string dir in _io.GetDirectories(root))
        {
            if (ContainsFileRecursive(dir, fileName, fs))
                return true;
        }

        return false;
    }

    private void ValidateUbiArtImport(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputPath))
            throw new ArgumentException("Input path is required for UbiArt imports.", nameof(request.InputPath));

        // Accept either a folder or an .ipk file. For .ipk, ensure the file exists; otherwise ensure the directory exists.
        if (Path.GetExtension(request.InputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            if (!_io.FileExists(request.InputPath))
                throw new FileNotFoundException("Input IPK file not found", request.InputPath);
        }
        else
        {
            if (!_io.DirectoryExists(request.InputPath))
                throw new FileNotFoundException("Input folder not found", request.InputPath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required for UbiArt imports.", nameof(request.OutputPath));

        // FileSystem must already be configured and initialized by caller
        ArgumentNullException.ThrowIfNull(fs);

        bool hasSongDesc = fs.TryGetSongDescriptorPath(fs.SongName, out _);

        bool hasJddb = _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "jddb.json"))
            || _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "..", "jddb.json"))
            || ContainsFileRecursive(fs.InputFolders.InputFolder, "jddb.json", fs);

        if (!hasSongDesc && !hasJddb)
            throw new FileNotFoundException("songdesc.tpl or jddb.json is required for UbiArt imports.");
    }
}
