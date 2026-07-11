using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public class JustDanceUbiArtFileSystem : IDisposable
{
    private readonly ILogger<JustDanceUbiArtFileSystem> _logger;
    private readonly UbiArtLayeredFileSystem _fileSystem;

    public JustDanceUbiArtFileSystem(
        UbiArtConversionRequest conversionRequest,
        UbiArtVersionProfile profile,
        ILogger<JustDanceUbiArtFileSystem> logger,
        IFileSystem io,
        ITempFolderManager tempManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConversionRequest = conversionRequest ?? throw new ArgumentNullException(nameof(conversionRequest));
        VersionProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(tempManager);

        _fileSystem = new(new UbiArtLayeredFileSystemOptions
        {
            InputPath = ConversionRequest.InputPath,
            Platform = VersionProfile.Platform,
            IsUncooked = ConversionRequest.Type == CookedType.Uncooked,
            AdditionalSearchRoots = GetAdditionalSearchRoots(ConversionRequest.InputPath, VersionProfile.Platform, io)
        }, new JdiUbiArtFileSystemAdapter(io));

        TempFolders = new(this, _logger, tempManager);
        InputFolders = new(this);
    }

    public JustDanceUbiArtFileSystem(UbiArtConversionRequest conversionRequest, UbiArtVersionProfile profile, ILogger<JustDanceUbiArtFileSystem> logger)
        : this(conversionRequest, profile, logger, new SystemFileSystem(), new SystemTempFolderManager())
    {
    }

    public UbiArtVersionProfile VersionProfile { get; private set; }
    public UbiArtConversionRequest ConversionRequest { get; private set; }
    public string SongName { get; private set; } = "";
    public string ContentSongName { get; private set; } = "";
    public bool IsLegacyMashupSelection { get; private set; }
    public string? LegacyMashupBaseSongName { get; private set; }
    public TempFolders TempFolders { get; private set; }
    public InputFolders InputFolders { get; private set; }
    public IUbiArtAssetResolver? AssetResolver => new FileSystemAssetResolver(VersionProfile.Layout, this);

    public void Initialize()
    {
        if (VersionProfile.Layout == null || VersionProfile.Serializer == null)
            throw new InvalidOperationException("FileSystem must be configured with a layout and serializer before initialization.");

        _fileSystem.Initialize();

        if (ConversionRequest.SongName != null)
            UpdateSongName(ConversionRequest.SongName);
    }

    public void InitializeSongID()
    {
        if (!string.IsNullOrWhiteSpace(SongName))
            return;

        (string SongName, string SongDescPath)[] songs = GetAvailableSongs();
        if (songs.Length == 0)
            throw new DirectoryNotFoundException("No song folders found in the maps folder.");
        if (songs.Length > 1)
            throw new DirectoryNotFoundException($"Multiple songs found in the bundle. Use GetAvailableSongs() to list them: {string.Join(", ", songs.Select(s => s.SongName))}");

        SongName = songs[0].SongName;
        UpdateSongRouting(SongName);
    }

    public (string SongName, string SongDescPath)[] GetAvailableSongs()
    {
        List<(string SongName, string SongDescPath)> songs = [];

        if (!string.IsNullOrWhiteSpace(ConversionRequest.SongName))
        {
            string contentSongName = ResolveContentSongName(ConversionRequest.SongName);
            string mapFolder = VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, contentSongName, VersionProfile.Platform, VersionProfile.EngineVersion)
                ?? Path.Combine("world", "maps", contentSongName);
            string songDescPath = TryGetSongDescriptorPath(ConversionRequest.SongName, out CookedFile? descriptor)
                ? descriptor.RelativePath
                : Path.Combine(mapFolder, "songdesc.tpl");
            songs.Add((ConversionRequest.SongName, songDescPath));
            return [.. songs];
        }

        string[] mapsLocations =
        [
            Path.Combine("world", "maps"),
            VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, string.Empty, VersionProfile.Platform, VersionProfile.EngineVersion) ?? string.Empty
        ];

        string[] songFolderNames = [];
        foreach (string mapsLocation in mapsLocations.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] songFolders = _fileSystem.GetInputDirectories(mapsLocation);
            if (songFolders.Length == 0)
                continue;

            songFolderNames =
            [
                .. songFolders
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
            ];
            break;
        }

        foreach (string songName in songFolderNames)
        {
            if (TryGetSongDescriptorPath(songName, out CookedFile? songDescPath))
            {
                songs.Add((songName, songDescPath.RelativePath));

                if (TryGetLegacyMashupTemplatePath(songName + "MU", out _))
                    songs.Add((songName + "MU", songDescPath.RelativePath));
            }
        }

        return
        [
            .. songs
                .GroupBy(song => song.SongName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(song => song.SongName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public bool TryGetSongDescriptorPath(string songName, [MaybeNullWhen(false)] out CookedFile descriptorPath)
    {
        descriptorPath = null;

        if (string.IsNullOrWhiteSpace(songName))
            return false;

        string contentSongName = ResolveContentSongName(songName);
        string mapFolder = VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, contentSongName, VersionProfile.Platform, VersionProfile.EngineVersion)
            ?? Path.Combine("world", "maps", contentSongName);

        string songNameLower = contentSongName.ToLowerInvariant();
        string[] candidates =
        [
            Path.Combine(mapFolder, "songdesc.tpl"),
            Path.Combine("cache", "legacyconverteddata", contentSongName, "songdesc.main_legacy.tpl"),
            Path.Combine("cache", "legacyconverteddata", songNameLower, "songdesc.main_legacy.tpl"),
            Path.Combine(mapFolder, "songdesc.act")
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GetFilePath(candidate, out CookedFile? found))
            {
                descriptorPath = found;
                return true;
            }
        }

        return false;
    }

    public void UpdateSongName(string? newSongName)
    {
        if (string.IsNullOrWhiteSpace(newSongName))
            return;

        if (string.Equals(SongName, newSongName, StringComparison.Ordinal))
            return;

        SongName = newSongName;
        ConversionRequest.SongName = newSongName;
        UpdateSongRouting(newSongName);
    }

    public bool GetFilePath(string relativeFilePath, [MaybeNullWhen(false)] out CookedFile filePath) => _fileSystem.GetFilePath(relativeFilePath, out filePath);
    public CookedFile GetFilePath(string relativeFilePath) => _fileSystem.GetFilePath(relativeFilePath);
    public CookedFile[] GetAllFiles(string relativeFolderPath, string pattern = "*") => _fileSystem.GetAllFiles(relativeFolderPath, pattern);
    public Stream GetFileStream(CookedFile cookedFile) => _fileSystem.GetFileStream(cookedFile);
    public bool GetFolderPath(string relativeFolderPath, [MaybeNullWhen(false)] out string folderPath) => _fileSystem.GetFolderPath(relativeFolderPath, out folderPath);
    public string GetFolderPath(string relativeFolderPath) => _fileSystem.GetFolderPath(relativeFolderPath);
    public void RegisterIPK(string ipkPath) => _fileSystem.RegisterIPK(ipkPath);
    public void DiscoverAndRegisterIPKs() => _fileSystem.DiscoverAndRegisterIPKs();
    public string ReadWithoutNull(string filePath) => _fileSystem.ReadWithoutNull(filePath);
    public string ReadWithoutNull(CookedFile cookedFile) => _fileSystem.ReadWithoutNull(cookedFile);
    public void Dispose()
    {
        _fileSystem.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool TryGetLegacyMashupTemplatePath(string songName, [MaybeNullWhen(false)] out CookedFile templatePath)
    {
        templatePath = null;

        if (VersionProfile.EngineVersion is not (UbiArtEngineVersion.JD2014 or UbiArtEngineVersion.JD2015) ||
            VersionProfile.Serializer is not BinaryUbiArtSerializer ||
            !TryGetLegacyMashupTemplateCandidatePath(songName, out CookedFile? candidate))
        {
            return false;
        }

        try
        {
            using Stream stream = GetFileStream(candidate);
            LegacyBlockFlow blockFlow = VersionProfile.Serializer.Deserialize<LegacyBlockFlow>(stream);
            if (blockFlow.ComponentCount == 0 ||
                blockFlow.Component is null ||
                blockFlow.Component.IsMashUp == 0 ||
                blockFlow.Component.BlockDescriptorVector.Length == 0)
            {
                return false;
            }

            templatePath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Ignoring invalid legacy mashup template '{TemplatePath}'.", candidate.RelativePath);
            return false;
        }
    }

    private bool TryGetLegacyMashupTemplateCandidatePath(string songName, [MaybeNullWhen(false)] out CookedFile templatePath)
    {
        templatePath = null;

        if (!TryGetLegacyMashupBaseSongName(songName, out string? baseSongName))
            return false;

        string timelineFolder = VersionProfile.Layout?.GetTimelineFolder(ConversionRequest.InputPath, baseSongName, VersionProfile.Platform, VersionProfile.EngineVersion)
            ?? Path.Combine("world", "maps", baseSongName, "timeline");
        string lowerBase = baseSongName.ToLowerInvariant();
        string[] candidates =
        [
            Path.Combine(timelineFolder, $"{baseSongName}mu.tpl"),
            Path.Combine(timelineFolder, $"{lowerBase}mu.tpl"),
            Path.Combine(timelineFolder, $"{baseSongName}MU.tpl")
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GetFilePath(candidate, out CookedFile? found))
            {
                templatePath = found;
                return true;
            }
        }

        return false;
    }

    public bool TryGetLegacyMashupBaseSongName(string songName, [MaybeNullWhen(false)] out string baseSongName)
    {
        baseSongName = null;
        if (!IsLegacyMashupName(songName))
            return false;

        string candidate = songName[..^2];
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        baseSongName = candidate;
        return true;
    }

    private string ResolveContentSongName(string songName) =>
        TryGetLegacyMashupBaseSongName(songName, out string? baseSongName) &&
        TryGetLegacyMashupTemplatePath(songName, out _)
            ? baseSongName
            : songName;

    private void UpdateSongRouting(string songName)
    {
        ContentSongName = ResolveContentSongName(songName);
        IsLegacyMashupSelection = !string.Equals(ContentSongName, songName, StringComparison.OrdinalIgnoreCase);
        LegacyMashupBaseSongName = IsLegacyMashupSelection ? ContentSongName : null;
    }

    private static bool IsLegacyMashupName(string songName) =>
        !string.IsNullOrWhiteSpace(songName) &&
        songName.EndsWith("MU", StringComparison.OrdinalIgnoreCase);

    private static string[] GetAdditionalSearchRoots(string inputPath, UbiArtPlatform platform, JDI.Services.IFileSystem io)
    {
        return
        [
            .. GetSharedPackageSearchRoots(inputPath, platform, io),
            .. GetDlcBaseSearchRoots(inputPath, platform, io)
        ];
    }

    private static string[] GetSharedPackageSearchRoots(string inputPath, UbiArtPlatform platform, JDI.Services.IFileSystem io)
    {
        if (platform == UbiArtPlatform.Uncooked || string.IsNullOrWhiteSpace(inputPath))
            return [];

        string? packageParent = GetPackageParentFolder(inputPath, io);
        if (string.IsNullOrWhiteSpace(packageParent) || !io.DirectoryExists(packageParent))
            return [];

        string platformName = platform.GetCookedFolderName();
        if (string.IsNullOrWhiteSpace(platformName))
            return [];

        string upperPlatformName = platformName.ToUpperInvariant();
        List<string> roots = [];
        AddPackageSearchRoot(roots, io, packageParent, $"Bundle_{upperPlatformName}");
        AddPackageSearchRoot(roots, io, packageParent, $"bundle_{platformName}");
        AddPackageSearchRoot(roots, io, packageParent, $"BlockFlows_{upperPlatformName}");
        AddPackageSearchRoot(roots, io, packageParent, $"blockflows_{platformName}");
        AddPackageSearchRoot(roots, io, packageParent, $"patch_{upperPlatformName}");
        AddPackageSearchRoot(roots, io, packageParent, $"Patch_{upperPlatformName}");
        return
        [
            .. roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(root => !string.Equals(Path.GetFullPath(root), Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
        ];
    }

    private static void AddPackageSearchRoot(
        List<string> roots,
        JDI.Services.IFileSystem io,
        string packageParent,
        string packageName)
    {
        string folder = io.Combine(packageParent, packageName);
        if (io.DirectoryExists(folder))
            roots.Add(folder);

        string ipk = folder + ".ipk";
        if (io.FileExists(ipk))
            roots.Add(ipk);
    }

    private static string[] GetDlcBaseSearchRoots(string inputPath, UbiArtPlatform platform, JDI.Services.IFileSystem io)
    {
        if (platform == UbiArtPlatform.Uncooked || string.IsNullOrWhiteSpace(inputPath))
            return [];

        string? packageParent = GetPackageParentFolder(inputPath, io);
        if (string.IsNullOrWhiteSpace(packageParent))
            return [];

        if (HasPlatformBundle(packageParent, platform, io))
            return [];

        string? titleFolder = Path.GetDirectoryName(packageParent);
        string? contentRoot = string.IsNullOrWhiteSpace(titleFolder) ? null : Path.GetDirectoryName(titleFolder);
        if (string.IsNullOrWhiteSpace(contentRoot) || !io.DirectoryExists(contentRoot))
            return [];

        return [contentRoot];
    }

    private static string? GetPackageParentFolder(string inputPath, JDI.Services.IFileSystem io)
    {
        if (Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(inputPath);

        if (io.DirectoryExists(inputPath))
            return Path.GetDirectoryName(inputPath);

        return null;
    }

    private static bool HasPlatformBundle(string folder, UbiArtPlatform platform, JDI.Services.IFileSystem io)
    {
        string platformName = platform.GetCookedFolderName();
        if (string.IsNullOrWhiteSpace(platformName))
            return false;

        string upperPlatformName = platformName.ToUpperInvariant();
        foreach (string bundleName in new[] { $"Bundle_{upperPlatformName}", $"bundle_{platformName}" })
        {
            if (io.FileExists(io.Combine(folder, bundleName + ".ipk")) ||
                io.DirectoryExists(io.Combine(folder, bundleName)))
            {
                return true;
            }
        }

        if (!io.DirectoryExists(folder))
            return false;

        string numberedBundleSuffix = "_" + upperPlatformName;
        foreach (string child in io.GetDirectories(folder))
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith("Bundle_", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(numberedBundleSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
