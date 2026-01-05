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

public sealed class UbiArtJdiFormat(ISongDataLoader songDataLoader, Func<UbiArtConversionRequest, LayeredFileSystem> fileSystemFactory, IUbiArtEngineDetector engineDetector, JDI.Services.IAudioConverter audioConverter, JDI.Services.IMediaProcessor mediaProcessor, JDI.Services.ITextureService textureService, ILogger<UbiArtJdiFormat> logger, JDI.Services.IFileSystem? io = null) : IJdiFormat
{
    private readonly ISongDataLoader _songDataLoader = songDataLoader;
    private readonly Func<UbiArtConversionRequest, LayeredFileSystem> _fileSystemFactory = fileSystemFactory;
    private readonly IUbiArtEngineDetector _engineDetector = engineDetector ?? throw new ArgumentNullException(nameof(engineDetector));
    private readonly JDI.Services.IAudioConverter _audioConverter = audioConverter;
    private readonly JDI.Services.IMediaProcessor _mediaProcessor = mediaProcessor;
    private readonly JDI.Services.ITextureService _textureService = textureService;
    private readonly ILogger<UbiArtJdiFormat> _logger = logger;
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

        LayeredFileSystem fileSystem = _fileSystemFactory(ubiRequest);
        fileSystem.Configure(profile);
        fileSystem.Initialize();

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
            if (fileSystem.EngineVersion == UbiArtEngineVersion.JD2014 || fileSystem.EngineVersion == UbiArtEngineVersion.JD2015 || fileSystem.Serializer is BinaryUbiArtSerializer)
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

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt export expects a UbiArtConversionRequest.", nameof(request));

        if (ubiRequest.Type != UbiArtType.Uncooked)
            throw new NotSupportedException("Only Uncooked export is supported for now.");

        string outputFolder = ubiRequest.OutputPath;
        if (!string.IsNullOrEmpty(ubiRequest.SongName))
        {
            outputFolder = _io.Combine(outputFolder, ubiRequest.SongName);
        }
        else if (importResult.Package.Metadata.MapName != null)
        {
            outputFolder = _io.Combine(outputFolder, importResult.Package.Metadata.MapName);
        }

        PrepareOutputDirectory(outputFolder);

        // Detect the best profile for the export (use materialized root if available)
        UbiArtVersionProfile exportProfile;
        if (!string.IsNullOrWhiteSpace(importResult.MaterializedRoot))
            exportProfile = _engineDetector.Detect(importResult.MaterializedRoot);
        else if (!string.IsNullOrWhiteSpace(ubiRequest.InputPath) && _io.DirectoryExists(ubiRequest.InputPath))
            exportProfile = _engineDetector.Detect(ubiRequest.InputPath);
        else
            exportProfile = new UbiArtVersionProfile(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());

        await UbiArtAssetWriter.ExportToUncookedAsync(importResult.Package, importResult.MaterializedRoot, outputFolder, _logger, exportProfile.Layout, exportProfile.ContainerStyle, exportProfile.EngineVersion);
    }

    private void PrepareOutputDirectory(string targetFolder)
    {
        if (_io.DirectoryExists(targetFolder))
            _io.DeleteDirectory(targetFolder, true);
        _io.CreateDirectory(targetFolder);
    }

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_io.DirectoryExists(path))
            return false;

        try
        {
            bool isCooked = _io.DirectoryExists(_io.Combine(path, "cache", "itf_cooked"));
            UbiArtVersionProfile profile = _engineDetector.Detect(path);
            UbiArtConversionRequest req = new(path, _io.GetTempPath(), null) { Type = profile.ContainerStyle == UbiArtContainerStyle.Uncooked ? UbiArtType.Uncooked : UbiArtType.Cooked };
            LayeredFileSystem fs = _fileSystemFactory(req);
            fs.Configure(profile);
            fs.Initialize();

            // Use FileSystem.GetFilePath like SongDataLoader does to correctly find songdesc.tpl
            string songDescRelativePath = _io.Combine(fs.InputFolders.MapWorldFolder, "songdesc.tpl");
            bool hasSongDesc = fs.GetFilePath(songDescRelativePath, out CookedFile? songDescCooked);

            bool hasJddb = _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "jddb.json"))
                || _io.FileExists(_io.Combine(fs.InputFolders.InputFolder, "..", "jddb.json"))
                || ContainsFileRecursive(fs.InputFolders.InputFolder, "jddb.json");

            if (!hasSongDesc && !hasJddb)
                return false;

            // Attempt to load song data to determine engine version and report platform
            try
            {
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

    private bool ContainsFileRecursive(string root, string fileName)
    {
        if (_io.GetFiles(root, fileName).Length > 0)
            return true;

        foreach (var dir in _io.GetDirectories(root))
        {
            if (ContainsFileRecursive(dir, fileName))
                return true;
        }

        return false;
    }

    private void ValidateUbiArtImport(UbiArtConversionRequest request, LayeredFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputPath))
            throw new ArgumentException("Input path is required for UbiArt imports.", nameof(request.InputPath));
        if (!_io.DirectoryExists(request.InputPath))
            throw new FileNotFoundException("Input folder not found", request.InputPath);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required for UbiArt imports.", nameof(request.OutputPath));

        // FileSystem must already be configured and initialized by caller
        ArgumentNullException.ThrowIfNull(fs);

        // Use layout-aware path resolution if available
        string songDescRelative;
        if (fs.Layout != null)
            songDescRelative = fs.Layout.GetSongDescRelativePath(fs.ConversionRequest.InputPath, fs.SongName, fs.ContainerStyle, fs.EngineVersion);
        else
            songDescRelative = _io.Combine(fs.InputFolders.MapWorldFolder, "songdesc.tpl");
        bool hasSongDesc = fs.GetFilePath(songDescRelative, out _);

        bool hasJddb = ContainsFileRecursive(fs.InputFolders.InputFolder, "jddb.json");

        if (!hasSongDesc && !hasJddb)
            throw new FileNotFoundException("songdesc.tpl or jddb.json is required for UbiArt imports.");
    }
}