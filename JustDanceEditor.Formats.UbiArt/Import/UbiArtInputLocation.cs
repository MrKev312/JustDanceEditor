namespace JustDanceEditor.Formats.UbiArt.Import;

internal readonly record struct UbiArtInputLocation(string RootPath, string? SongName, bool IsDirectMap)
{
    public static UbiArtInputLocation Resolve(string inputPath, string? songName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        if (!Directory.Exists(inputPath) || !ContainsSongDescriptor(inputPath))
            return new(inputPath, songName, false);

        DirectoryInfo mapFolder = new(Path.GetFullPath(inputPath));
        DirectoryInfo? mapsFolder = mapFolder.Parent;
        DirectoryInfo? worldFolder = mapsFolder?.Parent;
        if (mapsFolder == null || worldFolder == null ||
            !IsMapContainer(mapsFolder.Name) ||
            !worldFolder.Name.Equals("world", StringComparison.OrdinalIgnoreCase))
        {
            return new(inputPath, songName, false);
        }

        string mapName = mapFolder.Name;
        if (!string.IsNullOrWhiteSpace(songName) &&
            !mapName.Equals(songName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The selected song '{songName}' does not match direct map folder '{mapName}'.",
                nameof(songName));
        }

        DirectoryInfo? root = TryGetCookedRoot(worldFolder) ?? worldFolder.Parent;
        return root == null
            ? new(inputPath, songName, false)
            : new(root.FullName, mapName, true);
    }

    private static bool ContainsSongDescriptor(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Any(name => name != null && SongDescriptorNames.Contains(name, StringComparer.OrdinalIgnoreCase));

    private static bool IsMapContainer(string name) =>
        name.Equals("maps", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("jd2015", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("jd5", StringComparison.OrdinalIgnoreCase);

    private static DirectoryInfo? TryGetCookedRoot(DirectoryInfo worldFolder)
    {
        DirectoryInfo? platformFolder = worldFolder.Parent;
        DirectoryInfo? cookedFolder = platformFolder?.Parent;
        DirectoryInfo? cacheFolder = cookedFolder?.Parent;
        if (platformFolder == null || cookedFolder == null || cacheFolder == null ||
            !cookedFolder.Name.Equals("itf_cooked", StringComparison.OrdinalIgnoreCase) ||
            !cacheFolder.Name.Equals("cache", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cacheFolder.Parent;
    }

    private static readonly string[] SongDescriptorNames =
    [
        "songdesc.tpl",
        "songdesc.tpl.ckd",
        "songdesc.act",
        "songdesc.act.ckd"
    ];
}
