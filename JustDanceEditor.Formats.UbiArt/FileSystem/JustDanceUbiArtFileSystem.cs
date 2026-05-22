using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Assets;

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
            IsUncooked = ConversionRequest.Type == CookedType.Uncooked
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
    public TempFolders TempFolders { get; private set; }
    public InputFolders InputFolders { get; private set; }
    public IUbiArtAssetResolver? AssetResolver => new FileSystemAssetResolver(VersionProfile.Layout, this);

    public void Initialize()
    {
        if (VersionProfile.Layout == null || VersionProfile.Serializer == null)
            throw new InvalidOperationException("FileSystem must be configured with a layout and serializer before initialization.");

        _fileSystem.Initialize();

        if (ConversionRequest.SongName != null)
            SongName = ConversionRequest.SongName;
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
    }

    public (string SongName, string SongDescPath)[] GetAvailableSongs()
    {
        List<(string SongName, string SongDescPath)> songs = [];

        if (!string.IsNullOrWhiteSpace(ConversionRequest.SongName))
        {
            string mapFolder = VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, ConversionRequest.SongName, VersionProfile.Platform, VersionProfile.EngineVersion)
                ?? Path.Combine("world", "maps", ConversionRequest.SongName);
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
                songs.Add((songName, songDescPath.RelativePath));
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

        string mapFolder = VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, songName, VersionProfile.Platform, VersionProfile.EngineVersion)
            ?? Path.Combine("world", "maps", songName);

        string songNameLower = songName.ToLowerInvariant();
        string[] candidates =
        [
            Path.Combine(mapFolder, "songdesc.tpl"),
            Path.Combine("cache", "legacyconverteddata", songName, "songdesc.main_legacy.tpl"),
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
}