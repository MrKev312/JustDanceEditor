using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Intermediate;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtJdiFormat(ISongDataLoader songDataLoader, Func<UbiArtConversionRequest, UbiArtVersionProfile, LayeredFileSystem> fileSystemFactory, IUbiArtEngineDetector engineDetector, JDI.Services.IAudioConverter audioConverter, JDI.Services.IMediaProcessor mediaProcessor, JDI.Services.ITextureService textureService, IUbiArtAssetWriter assetWriter, ILogger<UbiArtJdiFormat> logger, JDI.Services.IFileSystem? io = null) : IJdiFormat
{
    private readonly ISongDataLoader _songDataLoader = songDataLoader;
    private readonly Func<UbiArtConversionRequest, UbiArtVersionProfile, LayeredFileSystem> _fileSystemFactory = fileSystemFactory;
    private readonly IUbiArtEngineDetector _engineDetector = engineDetector ?? throw new ArgumentNullException(nameof(engineDetector));
    private readonly JDI.Services.IAudioConverter _audioConverter = audioConverter;
    private readonly JDI.Services.IMediaProcessor _mediaProcessor = mediaProcessor;
    private readonly JDI.Services.ITextureService _textureService = textureService;
    private readonly ILogger<UbiArtJdiFormat> _logger = logger;
    private readonly IUbiArtAssetWriter _assetWriter = assetWriter;
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    public string DisplayName => "UbiArt";
    public bool CanImport => true;
    public bool CanExport => true;

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt import expects a UbiArtConversionRequest.", nameof(request));

        // Detect engine/profile and configure filesystem accordingly BEFORE validation so that file lookups work correctly
        UbiArtVersionProfile profile = _engineDetector.Detect(ubiRequest.InputPath);
        _logger.LogInformation("Detected engine container: {Container}, engine version: {Version}", profile.ContainerStyle, profile.EngineVersion);

        LayeredFileSystem fileSystem = _fileSystemFactory(ubiRequest, profile);
        fileSystem.Initialize();

        // Resolve selected song (may call into UI via request.SelectSongAsync)
        string chosenSong = await ResolveSongAsync(ubiRequest, fileSystem);
        if (!string.IsNullOrWhiteSpace(chosenSong))
            fileSystem.UpdateSongName(chosenSong);

        ValidateUbiArtImport(ubiRequest, fileSystem);

        // Log song name and platform here (moved from FileSystem internals)
        if (!string.IsNullOrWhiteSpace(fileSystem.SongName))
            _logger.LogInformation("Song name: {SongName}", fileSystem.SongName);

        if (!fileSystem.PlatformType.Equals("nx", StringComparison.CurrentCultureIgnoreCase))
            _logger.LogWarning("Platform: {Platform}, which is not officially supported. The conversion might not work as expected.", fileSystem.PlatformType);
        else
            _logger.LogInformation("Platform: {Platform}", fileSystem.PlatformType);

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
            // If the configured serializer is binary (JD2014/JD2015), provide a friendly message
            if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014 || fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015 || fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer)
                throw new NotSupportedException("JD2014/2015 binary support is coming soon.", ex);

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

        await IntermediateAssetWriter.PopulateFromUbiArtAsync(context, context.IntermediatePackage, outputFolder, _logger, _textureService);
        IntermediatePackageSerializer.WriteToFolder(context.IntermediatePackage, outputFolder);

        return new JdiImportResult(
            context.IntermediatePackage,
            "UbiArt",
            outputFolder,
            MaterializedRootIsTemporary: outputFolder.Contains(_io.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            SuggestedOutputFolder: outputFolder);
    }

    public async Task<string> ResolveSongAsync(UbiArtConversionRequest request, LayeredFileSystem fileSystem)
    {
        // If already specified, return it
        if (!string.IsNullOrWhiteSpace(fileSystem.SongName))
            return fileSystem.SongName;

        var available = fileSystem.GetAvailableSongs();
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

        if (ubiRequest.Type != UbiArtType.Uncooked)
            throw new NotSupportedException("Only Uncooked export is supported for now.");

        // Use the output path directly - the layout resolver will create World/Maps/SongName structure
        string outputFolder = ubiRequest.OutputPath;

        // Ensure output directory exists (don't delete it - it's a user-provided folder)
        _io.CreateDirectory(outputFolder);

        // Detect the best profile for the export (use materialized root if available)
        UbiArtVersionProfile exportProfile;
        if (!string.IsNullOrWhiteSpace(importResult.MaterializedRoot))
            exportProfile = _engineDetector.Detect(importResult.MaterializedRoot);
        else if (!string.IsNullOrWhiteSpace(ubiRequest.InputPath) && _io.DirectoryExists(ubiRequest.InputPath))
            exportProfile = _engineDetector.Detect(ubiRequest.InputPath);
        else
            exportProfile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());

        await _assetWriter.ExportToUncookedAsync(importResult.Package, importResult.MaterializedRoot, outputFolder, exportProfile.Layout, exportProfile.ContainerStyle, exportProfile.EngineVersion);
    }

    private void PrepareOutputDirectory(string targetFolder)
    {
        if (_io.DirectoryExists(targetFolder))
            _io.DeleteDirectory(targetFolder, true);
        _io.CreateDirectory(targetFolder);
    }

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!_io.DirectoryExists(path) && !Path.GetFileName(path).EndsWith(".ipk", StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            UbiArtVersionProfile profile = _engineDetector.Detect(path);
            UbiArtConversionRequest req = new(path, _io.GetTempPath(), null) { Type = profile.ContainerStyle == UbiArtContainerStyle.Uncooked ? UbiArtType.Uncooked : UbiArtType.Cooked };
            LayeredFileSystem fs = _fileSystemFactory(req, profile);
            fs.Initialize();

            // For Check, we just need to verify that maps exist with songdesc files
            // Don't try to initialize a specific song - just check if ANY songdesc.tpl exists in the maps folder
            var availableSongs = fs.GetAvailableSongs();
            if (availableSongs.Length == 0)
                return false;

            // Attempt to load song data from the first available song to determine engine version and report platform
            try
            {
                // Update filesystem with first song temporarily for verification
                fs.UpdateSongName(availableSongs[0].SongName);
                SongDesc sd = _songDataLoader.LoadSongDesc(req, fs);
                uint engine = sd.COMPONENTS[0].JDVersion;
                uint original = sd.COMPONENTS[0].OriginalJDVersion;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Detected UbiArt platform: {fs.PlatformType}, engine version: {engine}");
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

    private bool ContainsFileRecursive(string root, string fileName, LayeredFileSystem fs)
    {
        // If root points at an IPK file, use the LayeredFileSystem search helpers which are IPK-aware
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

        foreach (var dir in _io.GetDirectories(root))
        {
            if (ContainsFileRecursive(dir, fileName, fs))
                return true;
        }

        return false;
    }

    private void ValidateUbiArtImport(UbiArtConversionRequest request, LayeredFileSystem fs)
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

        // Use layout-aware path resolution if available
        string songDescRelative;
        if (fs.VersionProfile.Layout != null)
            songDescRelative = fs.VersionProfile.Layout.GetSongDescRelativePath(fs.ConversionRequest.InputPath, fs.SongName, fs.VersionProfile.ContainerStyle, fs.VersionProfile.EngineVersion);
        else
            songDescRelative = _io.Combine(fs.InputFolders.MapWorldFolder, "songdesc.tpl");
        bool hasSongDesc = fs.GetFilePath(songDescRelative, out _);

        bool hasJddb = _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "jddb.json"))
            || _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "..", "jddb.json"))
            || ContainsFileRecursive(fs.InputFolders.InputFolder, "jddb.json", fs);

        if (!hasSongDesc && !hasJddb)
            throw new FileNotFoundException("songdesc.tpl or jddb.json is required for UbiArt imports.");
    }
}