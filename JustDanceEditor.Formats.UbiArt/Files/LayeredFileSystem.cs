using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class LayeredFileSystem
{
    private readonly ILogger<LayeredFileSystem> _logger;
    private readonly IFileSystem _io;
    private readonly ITempFolderManager _tempManager;

    public IUbiArtLayout? Layout { get; private set; }
    public IUbiArtSerializer? Serializer { get; private set; }
    public UbiArtContainerStyle ContainerStyle { get; private set; } = UbiArtContainerStyle.Unknown;
    public UbiArtEngineVersion EngineVersion { get; private set; } = UbiArtEngineVersion.Unknown;

    public Services.Assets.IUbiArtAssetResolver? AssetResolver { get; private set; }
    public IUbiArtDataMapper? Mapper { get; private set; }

    public LayeredFileSystem(UbiArtConversionRequest conversionRequest, ILogger<LayeredFileSystem> logger, IFileSystem io, ITempFolderManager tempManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConversionRequest = conversionRequest;
        _io = io ?? throw new ArgumentNullException(nameof(io));
        _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));

        // Initialization that depends on a layout/serializer will be run later via Initialize()
        TempFolders = new(this, _logger, _tempManager);
        InputFolders = new(this);
    }

    // Backwards-compatible constructor for tests and existing callers; creates default System implementations
    public LayeredFileSystem(UbiArtConversionRequest conversionRequest, ILogger<LayeredFileSystem> logger)
        : this(conversionRequest, logger, new SystemFileSystem(), new SystemTempFolderManager())
    {
    }

    public void Configure(UbiArtVersionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Layout = profile.Layout ?? throw new ArgumentNullException(nameof(profile.Layout));
        Serializer = profile.Serializer ?? throw new ArgumentNullException(nameof(profile.Serializer));
        ContainerStyle = profile.ContainerStyle;
        EngineVersion = profile.EngineVersion;

        // Create an asset resolver tied to this filesystem
        AssetResolver = new Services.Assets.FileSystemAssetResolver(Layout, this);

        // Attach mapper for future fixups
        Mapper = profile.Mapper;
    }

    public void Initialize()
    {
        // Must be called after Configure()
        if (Layout == null || Serializer == null)
            throw new InvalidOperationException("FileSystem must be configured with a layout and serializer before initialization.");

        InitializeSongID();
        InitializePlatformType();

        // NOTE: LayeredFileSystem is read-only regarding file system mutations; temp folders are managed by ITempFolderManager and
        // should only be created by components that need them (e.g., exporters/converters).
    }

    public UbiArtConversionRequest ConversionRequest { get; private set; }

    public string SongName { get; private set; } = "";
    public string PlatformType { get; private set; } = "";

    public TempFolders TempFolders { get; private set; }
    public InputFolders InputFolders { get; private set; }

    public void UpdateSongName(string? newSongName)
    {
        if (string.IsNullOrWhiteSpace(newSongName))
            return;

        if (string.Equals(SongName, newSongName, StringComparison.Ordinal))
            return;

        string previousMapName = SongName;

        SongName = newSongName;
        ConversionRequest.SongName = newSongName;

        // Do not create or delete temp folders here; responsibility moved to ITempFolderManager consumers.
        // Cleanup is explicit and should be performed by the calling workflow when appropriate.
    }

    private void InitializeSongID()
    {
        if (ConversionRequest.SongName != null)
        {
            SongName = ConversionRequest.SongName;
            return;
        }

        // If the request explicitly says Uncooked or the detected container style is Uncooked, derive the song name from the input folder
        if (ConversionRequest.Type == UbiArtType.Uncooked || ContainerStyle == UbiArtContainerStyle.Uncooked)
        {
            SongName = Path.GetFileName(ConversionRequest.InputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return;
        }

        string mapsFolder = _io.Combine(ConversionRequest.InputPath, "world", "maps");
        if (!_io.DirectoryExists(mapsFolder))
        {
            // If our configured layout provides a different location, try that too
            if (Layout != null)
            {
                string candidate = _io.Combine(ConversionRequest.InputPath, Layout.GetMapWorldFolder(ConversionRequest.InputPath, string.Empty, ContainerStyle, EngineVersion));
                if (!string.IsNullOrWhiteSpace(candidate) && _io.DirectoryExists(candidate))
                    mapsFolder = candidate;
            }

            if (!_io.DirectoryExists(mapsFolder))
                throw new DirectoryNotFoundException("The maps folder does not exist.");
        }

        string[] songs = _io.GetDirectories(mapsFolder);
        if (songs.Length < 1)
            throw new DirectoryNotFoundException("No song folders found in the maps folder.");
        if (songs.Length > 1)
            throw new DirectoryNotFoundException("Multiple song folders found in the maps folder, please specify the song name in the request.");

        SongName = Path.GetFileName(songs[0]);
    }

    private void InitializePlatformType()
    {
        string itfCookedFolder = _io.Combine(ConversionRequest.InputPath, "cache", "itf_cooked");

        if (!_io.DirectoryExists(itfCookedFolder))
        {
            // If the request or the detected profile indicates Uncooked, set platform accordingly
            if (ContainerStyle == UbiArtContainerStyle.Uncooked || ConversionRequest.Type == UbiArtType.Uncooked)
            {
                PlatformType = "uncooked";
                return;
            }

            throw new DirectoryNotFoundException("The itf_cooked folder does not exist.");
        }

        string[] platformFolders = _io.GetDirectories(itfCookedFolder);

        if (platformFolders.Length == 0)
            throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
        if (platformFolders.Length > 1)
            throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

        PlatformType = Path.GetFileName(platformFolders[0]);

    }

    public bool GetFilePath(string relativeFilePath, [MaybeNullWhen(false)] out CookedFile filePath)
    {
        filePath = null;

        // For Uncooked, check the relative file path directly under InputPath first
        if (ConversionRequest.Type == UbiArtType.Uncooked)
        {
            string directChild = _io.Combine(ConversionRequest.InputPath, relativeFilePath);
            if (_io.FileExists(directChild))
            {
                filePath = new(directChild);
                return true;
            }

            // Check if it's world/maps/... but user pointed to the song folder
            // e.g. relativeFilePath = world/maps/songname/songdesc.tpl
            // but InputPath = .../songname
            // We can check the filename directly in InputPath
            string fileName = Path.GetFileName(relativeFilePath);
            string rootFile = _io.Combine(ConversionRequest.InputPath, fileName);
            if (_io.FileExists(rootFile))
            {
                filePath = new(rootFile);
                return true;
            }
        }

        string parentFolder = _io.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [_io.Combine(parentFolder, $"patch_{PlatformType}")];

        string[] allFolders = _io.GetDirectories(parentFolder);
        PriorityQueue<string, uint> numberPatternFolders = new();
        List<string> otherFolders = [];

        foreach (string folder in allFolders)
        {
            if (searchPaths.Contains(folder))
                continue;

            string folderName = Path.GetFileName(folder);
            string[] parts = folderName.Split('_');

            if (parts.Length < 3)
            {
                otherFolders.Add(folder);
                continue;
            }

            string secondToLastPart = parts[^2];

            if (uint.TryParse(secondToLastPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint number))
                numberPatternFolders.Enqueue(folder, number);
            else
                otherFolders.Add(folder);
        }

        while (numberPatternFolders.Count > 0)
            searchPaths.Add(numberPatternFolders.Dequeue());

        searchPaths.AddRange(otherFolders);

        string? pathCooked = null;

        if (Path.GetExtension(relativeFilePath) != ".ckd")
            pathCooked = $"{relativeFilePath}.ckd";

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                _io.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string file = _io.Combine(location, relativeFilePath);
                if (_io.FileExists(file))
                {
                    filePath = new(file);
                    return true;
                }

                if (pathCooked == null)
                    continue;

                file = _io.Combine(location, pathCooked);
                if (_io.FileExists(file))
                {
                    filePath = new(file);
                    return true;
                }
            }
        }

        return false;
    }

    public CookedFile GetFilePath(string relativeFilePath)
    {
        return GetFilePath(relativeFilePath, out CookedFile? filePath)
            ? filePath
            : throw new FileNotFoundException($"The file {relativeFilePath} was not found in the input folders.");
    }

    public CookedFile[] GetAllFiles(string relativeFolderPath, string pattern = "*")
    {
        List<CookedFile> files = [];
        string parentFolder = _io.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [
            _io.Combine(parentFolder, $"patch_{PlatformType}"),
            .._io.GetDirectories(parentFolder)
            ];

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                _io.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string folder = _io.Combine(location, relativeFolderPath);
                if (_io.DirectoryExists(folder))
                {
                    // Try the requested pattern and also the cooked variant (pattern.ckd) if applicable
                    List<string> patternsToTry = [pattern];
                    if (!pattern.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase))
                        patternsToTry.Add(pattern + ".ckd");

                    foreach (string pat in patternsToTry)
                    {
                        foreach (string file in _io.GetFiles(folder, pat))
                        {
                            string relative = Path.GetRelativePath(searchPath, file);
                            if (files.Any(x => x.FullPath.EndsWith(relative, StringComparison.CurrentCultureIgnoreCase)))
                                continue;

                            files.Add(new(file));
                        }
                    }
                }
            }
        }

        return [.. files];
    }

    public bool GetFolderPath(string relativeFolderPath, [MaybeNullWhen(false)] out string folderPath)
    {
        folderPath = null;
        string parentFolder = _io.Combine(InputFolders.InputFolder, "..");

        List<string> searchPaths = [
            _io.Combine(parentFolder, $"patch_{PlatformType}"),
            InputFolders.InputFolder,
            _io.Combine(parentFolder, $"bundle_{PlatformType}")
            ];

        foreach (string searchPath in _io.GetDirectories(parentFolder))
            if (!searchPaths.Contains(searchPath))
                searchPaths.Add(searchPath);

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                _io.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];
            foreach (string location in searchLocations)
            {
                string folder = _io.Combine(location, relativeFolderPath);
                if (_io.DirectoryExists(folder))
                {
                    folderPath = folder;
                    return true;
                }
            }
        }

        return false;
    }

    public string GetFolderPath(string relativeFolderPath)
    {
        return GetFolderPath(relativeFolderPath, out string? folderPath)
            ? folderPath
            : throw new DirectoryNotFoundException($"The folder {relativeFolderPath} was not found in the input folders.");
    }

    public string ReadWithoutNull(string filePath)
    {
        return _io.ReadAllText(filePath).TrimEnd('\0');
    }
}