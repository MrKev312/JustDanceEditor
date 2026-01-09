using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class LayeredFileSystem
{
    private readonly ILogger<LayeredFileSystem> _logger;
    private readonly IFileSystem _io;
    private readonly ITempFolderManager _tempManager;
    private readonly Dictionary<string, IpkFileSystem> _ipkFileSystems = [];

    public UbiArtVersionProfile VersionProfile { get; private set; }

    public Services.Assets.IUbiArtAssetResolver? AssetResolver => new Services.Assets.FileSystemAssetResolver(VersionProfile.Layout, this);

    public LayeredFileSystem(UbiArtConversionRequest conversionRequest, UbiArtVersionProfile profile, ILogger<LayeredFileSystem> logger, IFileSystem io, ITempFolderManager tempManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConversionRequest = conversionRequest;
        _io = io ?? throw new ArgumentNullException(nameof(io));
        _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));

        // Create an asset resolver tied to this filesystem
        //AssetResolver = new Services.Assets.FileSystemAssetResolver(Layout, this);

        // Initialization that depends on a layout/serializer will be run later via Initialize()
        VersionProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        TempFolders = new(this, _logger, _tempManager);
        InputFolders = new(this);
    }

    // Backwards-compatible constructor for tests and existing callers; creates default System implementations
    public LayeredFileSystem(UbiArtConversionRequest conversionRequest, UbiArtVersionProfile profile, ILogger<LayeredFileSystem> logger)
        : this(conversionRequest, profile, logger, new SystemFileSystem(), new SystemTempFolderManager())
    {
    }

    public void Initialize()
    {
        // Must be called after Configure()
        if (VersionProfile.Layout == null || VersionProfile.Serializer == null)
            throw new InvalidOperationException("FileSystem must be configured with a layout and serializer before initialization.");

        // If the input is an IPK, ensure it's registered and discover other IPKs in the same folder
        if (Path.GetExtension(ConversionRequest.InputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            RegisterIPK(ConversionRequest.InputPath);
            DiscoverAndRegisterIPKs();
        }
        else
        {
            // For directory inputs, still discover adjacent IPKs for fallback lookups
            DiscoverAndRegisterIPKs();
        }

        InitializePlatformType();

        // Only initialize SongID if explicitly requested or if a specific song name was provided
        if (ConversionRequest.SongName != null)
        {
            SongName = ConversionRequest.SongName;
        }

        // NOTE: LayeredFileSystem is read-only regarding file system mutations; temp folders are managed by ITempFolderManager and
        // should only be created by components that need them (e.g., exporters/converters).
    }

    /// <summary>
    /// Explicitly initialize the song name. Call this after Initialize() when you need to determine the song.
    /// For multi-song bundles, this will throw - use GetAvailableSongs() instead.
    /// </summary>
    public void InitializeSongID()
    {
        if (!string.IsNullOrWhiteSpace(SongName))
            return; // Already initialized

        (string SongName, string SongDescPath)[] songs = GetAvailableSongs();
        if (songs.Length == 0)
            throw new DirectoryNotFoundException("No song folders found in the maps folder.");
        if (songs.Length > 1)
            throw new DirectoryNotFoundException($"Multiple songs found in the bundle. Use GetAvailableSongs() to list them: {string.Join(", ", songs.Select(s => s.SongName))}");

        SongName = songs[0].SongName;
    }

    /// <summary>
    /// Get all available songs in the current bundle/input, discovering from maps folders.
    /// Returns an array of (SongName, SongDescPath) tuples.
    /// Gets all folder names from the input's maps directory, then searches all bundles for their songdesc files.
    /// </summary>
    public (string SongName, string SongDescPath)[] GetAvailableSongs()
    {
        List<(string SongName, string SongDescPath)> songs = [];

        // For explicitly specified song, return it immediately
        if (!string.IsNullOrWhiteSpace(ConversionRequest.SongName))
        {
            string mapFolder = VersionProfile.Layout?.GetMapWorldFolder(ConversionRequest.InputPath, ConversionRequest.SongName, VersionProfile.ContainerStyle, VersionProfile.EngineVersion)
                ?? Path.Combine("world", "maps", ConversionRequest.SongName);
            string songDescPath = Path.Combine(mapFolder, "songdesc.tpl");
            songs.Add((ConversionRequest.SongName, songDescPath));
            return [.. songs];
        }

        // Get all folder names in maps from the input (whether IPK or directory)
        string[] songFolderNames = [];

        if (Path.GetExtension(ConversionRequest.InputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            if (_ipkFileSystems.TryGetValue(Path.GetFileNameWithoutExtension(ConversionRequest.InputPath), out IpkFileSystem? ipk))
            {
                string mapsRel = Path.Combine("world", "maps");
                if (ipk.DirectoryExists(mapsRel))
                {
                    string[] songFolders = ipk.GetDirectories(mapsRel);
                    songFolderNames = [.. songFolders.Select(f => Path.GetFileName(f))];
                }
            }
        }
        else
        {
            // For directory inputs, get folder names from maps
            string mapsFolder = _io.Combine(ConversionRequest.InputPath, "world", "maps");
            if (!_io.DirectoryExists(mapsFolder))
            {
                // Try layout-specific location
                if (VersionProfile.Layout != null)
                {
                    string layoutMapsFolder = _io.Combine(ConversionRequest.InputPath, VersionProfile.Layout.GetMapWorldFolder(ConversionRequest.InputPath, "", VersionProfile.ContainerStyle, VersionProfile.EngineVersion));
                    if (_io.DirectoryExists(layoutMapsFolder))
                        mapsFolder = layoutMapsFolder;
                }
            }

            if (_io.DirectoryExists(mapsFolder))
            {
                string[] songDirs = _io.GetDirectories(mapsFolder);
                songFolderNames = [.. songDirs.Select(d => Path.GetFileName(d))];
            }
        }

        // For each song folder name, search all bundles for its songdesc using GetFilePath
        foreach (string songName in songFolderNames)
        {
            string songDescRel = Path.Combine("world", "maps", songName, "songdesc.tpl");
            if (GetFilePath(songDescRel, out CookedFile? songDescPath))
            {
                songs.Add((songName, songDescPath.RelativePath));
            }
        }

        return [.. songs];
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

    private void InitializePlatformType()
    {
        string itfCookedFolder = _io.Combine(ConversionRequest.InputPath, "cache", "itf_cooked");

        // If the input is an IPK, check registered IPKs for cache/itf_cooked
        if (Path.GetExtension(ConversionRequest.InputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            string rel = Path.Combine("cache", "itf_cooked");
            foreach (IpkFileSystem ipk in _ipkFileSystems.Values)
            {
                try
                {
                    if (ipk.DirectoryExists(rel))
                    {
                        string[] ipkPlatformFolders = ipk.GetDirectories(rel);
                        if (ipkPlatformFolders.Length == 0)
                            throw new DirectoryNotFoundException("No platform folders found in the itf_cooked folder.");
                        if (ipkPlatformFolders.Length > 1)
                            throw new DirectoryNotFoundException("Multiple platform folders found in the itf_cooked folder, this is not supported.");

                        PlatformType = Path.GetFileName(ipkPlatformFolders[0]);
                        return;
                    }
                }
                catch
                {
                    // Ignore IPK read errors and try next
                }
            }

            // If not found in IPK, fall through to the directory-based logic which will handle layout overrides or throw
        }

        if (!_io.DirectoryExists(itfCookedFolder))
        {
            // If the request or the detected profile indicates Uncooked, set platform accordingly
            if (VersionProfile.ContainerStyle == UbiArtContainerStyle.Uncooked || ConversionRequest.Type == UbiArtType.Uncooked)
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
                string relative = Path.GetRelativePath(ConversionRequest.InputPath, directChild).Replace('\\', Path.DirectorySeparatorChar);
                filePath = new(relative);
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
                string relative = Path.GetRelativePath(ConversionRequest.InputPath, rootFile).Replace('\\', Path.DirectorySeparatorChar);
                filePath = new(relative);
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
                    filePath = new(relativeFilePath);
                    return true;
                }

                if (pathCooked == null)
                    continue;

                file = _io.Combine(location, pathCooked);
                if (_io.FileExists(file))
                {
                    filePath = new(pathCooked);
                    return true;
                }
            }
        }

        // Now check IPKs, but only if a corresponding folder doesn't exist
        foreach (KeyValuePair<string, IpkFileSystem> ipkEntry in _ipkFileSystems)
        {
            string ipkName = ipkEntry.Key;
            IpkFileSystem ipk = ipkEntry.Value;

            // Skip if a folder with this name exists
            if (allFolders.Any(f => Path.GetFileNameWithoutExtension(f).Equals(ipkName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Build candidate paths to check inside the IPK (root and cache/itf_cooked/<PlatformType>)
            List<string> candidates = [relativeFilePath];
            if (pathCooked != null)
                candidates.Add(pathCooked);
            if (!string.IsNullOrEmpty(PlatformType))
            {
                string cookedPrefix = Path.Combine("cache", "itf_cooked", PlatformType);
                candidates.Add(Path.Combine(cookedPrefix, relativeFilePath));
                if (pathCooked != null)
                    candidates.Add(Path.Combine(cookedPrefix, pathCooked));
            }

            foreach (string cand in candidates)
            {
                try
                {
                    if (ipk.FileExists(cand))
                    {
                        filePath = new(cand);
                        return true;
                    }
                }
                catch
                {
                    // Ignore read errors on this IPK and continue to next candidate/IPK
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
                            string relative = Path.GetRelativePath(searchPath, file).Replace('\\', Path.DirectorySeparatorChar);
                            if (files.Any(x => x.RelativePath.EndsWith(relative, StringComparison.CurrentCultureIgnoreCase)))
                                continue;

                            files.Add(new(relative));
                        }
                    }
                }
            }
        }

        // Now check IPKs, but only if a corresponding folder doesn't exist
        string[] allFolders = _io.GetDirectories(parentFolder);
        foreach (KeyValuePair<string, IpkFileSystem> ipkEntry in _ipkFileSystems)
        {
            string ipkName = ipkEntry.Key;
            IpkFileSystem ipk = ipkEntry.Value;

            // Skip if a folder with this name exists
            if (allFolders.Any(f => Path.GetFileNameWithoutExtension(f).Equals(ipkName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Try both the requested relative folder and the cooked location inside IPK
            List<string> ipkLocations = [relativeFolderPath];
            if (!string.IsNullOrEmpty(PlatformType))
            {
                ipkLocations.Add(Path.Combine("cache", "itf_cooked", PlatformType, relativeFolderPath));
            }

            foreach (string loc in ipkLocations)
            {
                try
                {
                    if (!ipk.DirectoryExists(loc))
                        continue;

                    List<string> patternsToTry = [pattern];
                    if (!pattern.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase))
                        patternsToTry.Add(pattern + ".ckd");

                    foreach (string pat in patternsToTry)
                    {
                        foreach (string file in ipk.GetFiles(loc, pat))
                        {
                            string candidateRelative = Path.Combine(loc, file).Replace('\\', Path.DirectorySeparatorChar);
                            if (files.Any(x => x.RelativePath.EndsWith(candidateRelative, StringComparison.CurrentCultureIgnoreCase)))
                                continue;

                            files.Add(new(candidateRelative));
                        }
                    }
                }
                catch
                {
                    // Ignore problematic IPKs and continue
                }
            }
        }

        return [.. files];
    }

    public Stream GetFileStream(CookedFile cookedFile)
    {
        ArgumentNullException.ThrowIfNull(cookedFile);
        string relativeFilePath = cookedFile.RelativePath;

        // For Uncooked, try direct paths under the input folder first
        if (ConversionRequest.Type == UbiArtType.Uncooked)
        {
            string directChild = _io.Combine(ConversionRequest.InputPath, relativeFilePath);
            if (_io.FileExists(directChild))
                return File.OpenRead(directChild);

            string fileName = Path.GetFileName(relativeFilePath);
            string rootFile = _io.Combine(ConversionRequest.InputPath, fileName);
            if (_io.FileExists(rootFile))
                return File.OpenRead(rootFile);
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
                    return File.OpenRead(file);

                if (pathCooked == null)
                    continue;

                file = _io.Combine(location, pathCooked);
                if (_io.FileExists(file))
                    return File.OpenRead(file);
            }
        }

        // Now check IPKs, but only if a corresponding folder doesn't exist
        foreach (KeyValuePair<string, IpkFileSystem> ipkEntry in _ipkFileSystems)
        {
            string ipkName = ipkEntry.Key;
            IpkFileSystem ipk = ipkEntry.Value;

            // Skip if a folder with this name exists
            if (allFolders.Any(f => Path.GetFileNameWithoutExtension(f).Equals(ipkName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Build candidate paths to check inside the IPK (root and cache/itf_cooked/<PlatformType>)
            List<string> candidates = [relativeFilePath];
            if (pathCooked != null)
                candidates.Add(pathCooked);
            if (!string.IsNullOrEmpty(PlatformType))
            {
                string cookedPrefix = Path.Combine("cache", "itf_cooked", PlatformType);
                candidates.Add(Path.Combine(cookedPrefix, relativeFilePath));
                if (pathCooked != null)
                    candidates.Add(Path.Combine(cookedPrefix, pathCooked));
            }

            foreach (string cand in candidates)
            {
                try
                {
                    if (ipk.FileExists(cand))
                    {
                        byte[] data = ipk.ReadAllBytes(cand);
                        return new MemoryStream(data, writable: false);
                    }
                }
                catch
                {
                    // Ignore read errors on this IPK and continue
                }
            }
        }

        throw new FileNotFoundException($"The file {relativeFilePath} was not found in the input folders.");
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

        // Now check IPKs, but only if a corresponding folder doesn't exist
        string[] allFolders = _io.GetDirectories(parentFolder);
        foreach (KeyValuePair<string, IpkFileSystem> ipkEntry in _ipkFileSystems)
        {
            string ipkName = ipkEntry.Key;
            IpkFileSystem ipk = ipkEntry.Value;

            // Skip if a folder with this name exists
            if (allFolders.Any(f => Path.GetFileNameWithoutExtension(f).Equals(ipkName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Check both the requested relative folder and the cooked location inside IPK
            List<string> ipkLocations = [relativeFolderPath];
            if (!string.IsNullOrEmpty(PlatformType))
                ipkLocations.Add(Path.Combine("cache", "itf_cooked", PlatformType, relativeFolderPath));

            foreach (string loc in ipkLocations)
            {
                try
                {
                    if (ipk.DirectoryExists(loc))
                    {
                        folderPath = loc;  // For IPK, return the relative path inside the IPK
                        return true;
                    }
                }
                catch
                {
                    // Ignore problematic IPKs and continue
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

    /// <summary>
    /// Register an IPK file to be searched during file resolution.
    /// The IPK will be searched only if no corresponding folder exists (e.g., patch_nx.ipk only searched if no patch_nx folder exists).
    /// </summary>
    public void RegisterIPK(string ipkPath)
    {
        ArgumentNullException.ThrowIfNull(ipkPath);
        string ipkName = Path.GetFileNameWithoutExtension(ipkPath);
        if (!_ipkFileSystems.ContainsKey(ipkName))
        {
            _ipkFileSystems[ipkName] = new(ipkPath);
        }
    }

    /// <summary>
    /// Auto-discover and register IPK files from the input folder parent directory.
    /// Scans for *.ipk files and registers them, following the priority rule:
    /// patch_nx (folder) > patch_nx.ipk > regular folders > other.ipk files (if no other folder exists).
    /// </summary>
    public void DiscoverAndRegisterIPKs()
    {
        string parentFolder = _io.Combine(InputFolders.InputFolder, "..");

        try
        {
            string[] ipkFiles = _io.GetFiles(parentFolder, "*.ipk");
            foreach (string ipkPath in ipkFiles)
            {
                RegisterIPK(ipkPath);
            }
        }
        catch
        {
            // Silently ignore errors during IPK discovery (e.g., no read permissions)
        }
    }

    public string ReadWithoutNull(string filePath)
    {
        return _io.ReadAllText(filePath).TrimEnd('\0');
    }

    /// <summary>
    /// Read the specified cooked file as text, trimming any trailing NUL characters.
    /// Uses the same file resolution logic as <see cref="GetFileStream(CookedFile)"/>.
    /// </summary>
    public string ReadWithoutNull(CookedFile cookedFile)
    {
        using Stream s = GetFileStream(cookedFile);
        using StreamReader sr = new(s, System.Text.Encoding.UTF8);
        return sr.ReadToEnd().TrimEnd('\0');
    }
}