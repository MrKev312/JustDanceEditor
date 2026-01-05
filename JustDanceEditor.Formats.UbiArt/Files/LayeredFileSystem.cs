using JustDanceEditor.Formats.JDI;
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
    private readonly Formats.JDI.Services.IFileSystem _io;
    private readonly ITempFolderManager _tempManager;

    public IUbiArtLayout? Layout { get; private set; }
    public IUbiArtSerializer? Serializer { get; private set; }
    public UbiArtContainerStyle ContainerStyle { get; private set; } = UbiArtContainerStyle.Unknown;
    public UbiArtEngineVersion EngineVersion { get; private set; } = UbiArtEngineVersion.Unknown;

    public Services.Assets.IUbiArtAssetResolver? AssetResolver { get; private set; }
    public IUbiArtDataMapper? Mapper { get; private set; }

    public LayeredFileSystem(UbiArtConversionRequest conversionRequest, ILogger<LayeredFileSystem> logger, Formats.JDI.Services.IFileSystem io, ITempFolderManager tempManager)
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

        string mapsFolder = Path.Combine(ConversionRequest.InputPath, "world", "maps");
        if (!Directory.Exists(mapsFolder))
        {
            // If our configured layout provides a different location, try that too
            if (Layout != null)
            {
                string candidate = Path.Combine(ConversionRequest.InputPath, Layout.GetMapWorldFolder(ConversionRequest.InputPath, string.Empty, ContainerStyle, EngineVersion));
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    mapsFolder = candidate;
            }

            if (!Directory.Exists(mapsFolder))
                throw new DirectoryNotFoundException("The maps folder does not exist.");
        }

        string[] songs = Directory.GetDirectories(mapsFolder);
        if (songs.Length < 1)
            throw new DirectoryNotFoundException("No song folders found in the maps folder.");
        if (songs.Length > 1)
            throw new DirectoryNotFoundException("Multiple song folders found in the maps folder, please specify the song name in the request.");

        SongName = Path.GetFileName(songs[0]);
    }

    private void InitializePlatformType()
    {
        string itfCookedFolder = Path.Combine(ConversionRequest.InputPath, "cache", "itf_cooked");

        if (!Directory.Exists(itfCookedFolder))
        {
            // If the request or the detected profile indicates Uncooked, set platform accordingly
            if (ContainerStyle == UbiArtContainerStyle.Uncooked || ConversionRequest.Type == UbiArtType.Uncooked)
            {
                PlatformType = "uncooked";
                return;
            }

            throw new DirectoryNotFoundException("The itf_cooked folder does not exist.");
        }

        string[] platformFolders = Directory.GetDirectories(itfCookedFolder);

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
            string directChild = Path.Combine(ConversionRequest.InputPath, relativeFilePath);
            if (File.Exists(directChild))
            {
                filePath = new(directChild);
                return true;
            }

            // Check if it's world/maps/... but user pointed to the song folder
            // e.g. relativeFilePath = world/maps/songname/songdesc.tpl
            // but InputPath = .../songname
            // We can check the filename directly in InputPath
            string fileName = Path.GetFileName(relativeFilePath);
            string rootFile = Path.Combine(ConversionRequest.InputPath, fileName);
            if (File.Exists(rootFile))
            {
                filePath = new(rootFile);
                return true;
            }
        }

        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [Path.Combine(parentFolder, $"patch_{PlatformType}")];

        string[] allFolders = Directory.GetDirectories(parentFolder);
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
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string file = Path.Combine(location, relativeFilePath);
                if (File.Exists(file))
                {
                    filePath = new(file);
                    return true;
                }

                if (pathCooked == null)
                    continue;

                file = Path.Combine(location, pathCooked);
                if (File.Exists(file))
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
        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");
        List<string> searchPaths = [
            Path.Combine(parentFolder, $"patch_{PlatformType}"),
            ..Directory.GetDirectories(parentFolder)
            ];

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];

            foreach (string location in searchLocations)
            {
                string folder = Path.Combine(location, relativeFolderPath);
                if (Directory.Exists(folder))
                {
                    // Try the requested pattern and also the cooked variant (pattern.ckd) if applicable
                    List<string> patternsToTry = [pattern];
                    if (!pattern.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase))
                        patternsToTry.Add(pattern + ".ckd");

                    foreach (string pat in patternsToTry)
                    {
                        foreach (string file in Directory.GetFiles(folder, pat))
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
        string parentFolder = Path.Combine(InputFolders.InputFolder, "..");

        List<string> searchPaths = [
            Path.Combine(parentFolder, $"patch_{PlatformType}"),
            InputFolders.InputFolder,
            Path.Combine(parentFolder, $"bundle_{PlatformType}")
            ];

        foreach (string searchPath in Directory.GetDirectories(parentFolder))
            if (!searchPaths.Contains(searchPath))
                searchPaths.Add(searchPath);

        foreach (string searchPath in searchPaths)
        {
            string[] searchLocations = [
                searchPath,
                Path.Combine(searchPath, "cache", "itf_cooked", PlatformType)
                ];
            foreach (string location in searchLocations)
            {
                string folder = Path.Combine(location, relativeFolderPath);
                if (Directory.Exists(folder))
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

    public static string ReadWithoutNull(string filePath)
    {
        return File.ReadAllText(filePath).TrimEnd('\0');
    }
}