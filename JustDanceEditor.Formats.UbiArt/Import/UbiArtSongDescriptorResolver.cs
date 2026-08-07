using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import;

internal sealed class UbiArtSongDescriptorResolver : IDisposable
{
    private readonly IUbiArtFileSystem _directFileSystem;
    private readonly string _basePath;
    private readonly string? _cookedPlatformRoot;
    private readonly string _mapsFolder;
    private readonly UbiArtLayeredFileSystem? _layeredFileSystem;

    public UbiArtSongDescriptorResolver(
        IUbiArtFileSystem directFileSystem,
        string basePath,
        string? cookedPlatformRoot,
        string mapsFolder,
        string? sourcePath,
        UbiArtPlatform platform)
    {
        _directFileSystem = directFileSystem;
        _basePath = basePath;
        _cookedPlatformRoot = cookedPlatformRoot;
        _mapsFolder = mapsFolder;

        if (platform == UbiArtPlatform.Uncooked ||
            string.IsNullOrWhiteSpace(sourcePath) ||
            (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)))
        {
            return;
        }

        _layeredFileSystem = UbiArtLayeredFileSystemFactory.Create(
            new UbiArtLayeredFileSystemOptions
            {
                InputPath = sourcePath,
                Platform = platform,
                IsUncooked = false
            },
            prioritizeDirectoryInput: true);
        _layeredFileSystem.Initialize();
    }

    public string? ResolveMapName(string? requestedMapName)
    {
        if (!string.IsNullOrWhiteSpace(requestedMapName) || string.IsNullOrWhiteSpace(_mapsFolder))
            return requestedMapName;

        IEnumerable<string> directories = _layeredFileSystem != null
            ? _layeredFileSystem.GetInputDirectories(_mapsFolder)
            : GetDirectMapDirectories();
        string[] mapNames =
        [
            .. directories
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        return mapNames.Length == 1 ? mapNames[0] : null;
    }

    public bool TryReadModern(string? mapName, out string content, out bool isUncooked)
    {
        content = string.Empty;
        isUncooked = false;
        if (string.IsNullOrWhiteSpace(mapName) && !string.IsNullOrWhiteSpace(_mapsFolder))
            return false;

        string relativePath = string.IsNullOrWhiteSpace(_mapsFolder)
            ? "songdesc.tpl"
            : Path.Combine(_mapsFolder, mapName!, "songdesc.tpl");
        return TryReadText(relativePath, out content, out isUncooked);
    }

    public bool TryReadLegacy(string? mapName, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(mapName))
            return false;

        string[] candidates =
        [
            Path.Combine("cache", "legacyconverteddata", mapName, "songdesc.main_legacy.tpl"),
            Path.Combine("cache", "legacyconverteddata", mapName.ToLowerInvariant(), "songdesc.main_legacy.tpl")
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryReadBytes(candidate, out bytes))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _layeredFileSystem?.Dispose();
        GC.SuppressFinalize(this);
    }

    private IEnumerable<string> GetDirectMapDirectories()
    {
        string[] roots = _cookedPlatformRoot == null ? [_basePath] : [_basePath, _cookedPlatformRoot];
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);

        foreach (string root in roots)
        {
            string mapsPath = _directFileSystem.Combine(root, _mapsFolder);
            if (!_directFileSystem.DirectoryExists(mapsPath))
                continue;

            foreach (string directory in _directFileSystem.GetDirectories(mapsPath))
                directories.Add(directory);
        }

        return directories;
    }

    private bool TryReadText(string relativePath, out string content, out bool isUncooked)
    {
        content = string.Empty;
        if (_layeredFileSystem != null && _layeredFileSystem.GetFilePath(relativePath, out CookedFile? layeredPath))
        {
            content = _layeredFileSystem.ReadWithoutNull(layeredPath);
            isUncooked = !layeredPath.IsCooked;
            return true;
        }

        if (TryGetDirectUncookedPath(relativePath, out CookedFile? uncookedPath))
        {
            content = _directFileSystem.ReadAllText(uncookedPath.RelativePath).TrimEnd('\0');
            isUncooked = true;
            return true;
        }

        if (TryGetDirectCookedPath(relativePath, out CookedFile? cookedPath))
        {
            content = _directFileSystem.ReadAllText(cookedPath.RelativePath).TrimEnd('\0');
            isUncooked = false;
            return true;
        }

        isUncooked = false;
        return false;
    }

    private bool TryReadBytes(string relativePath, out byte[] bytes)
    {
        if (_layeredFileSystem != null && _layeredFileSystem.GetFilePath(relativePath, out CookedFile? layeredPath))
        {
            using Stream stream = _layeredFileSystem.GetFileStream(layeredPath);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            return true;
        }

        if (TryGetDirectUncookedPath(relativePath, out CookedFile? uncookedPath))
        {
            bytes = _directFileSystem.ReadAllBytes(uncookedPath.RelativePath);
            return true;
        }

        if (TryGetDirectCookedPath(relativePath, out CookedFile? cookedPath))
        {
            bytes = _directFileSystem.ReadAllBytes(cookedPath.RelativePath);
            return true;
        }

        bytes = [];
        return false;
    }

    private bool TryGetDirectUncookedPath(string relativePath, out CookedFile file)
    {
        string uncookedPath = _directFileSystem.Combine(_basePath, relativePath);
        if (_directFileSystem.FileExists(uncookedPath))
        {
            file = new(uncookedPath);
            return true;
        }

        file = null!;
        return false;
    }

    private bool TryGetDirectCookedPath(string relativePath, out CookedFile file)
    {
        if (_cookedPlatformRoot != null)
        {
            string cookedPath = _directFileSystem.Combine(_cookedPlatformRoot, relativePath + ".ckd");
            if (_directFileSystem.FileExists(cookedPath))
            {
                file = new(cookedPath);
                return true;
            }
        }

        file = null!;
        return false;
    }
}
