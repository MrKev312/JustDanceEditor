using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.FileSystem;

internal static class UbiArtLayeredFileSystemFactory
{
    public static UbiArtLayeredFileSystem Create(
        UbiArtLayeredFileSystemOptions options,
        IUbiArtFileSystem? io = null,
        bool prioritizeDirectoryInput = false)
    {
        IUbiArtFileSystem fileSystem = io ?? new PhysicalUbiArtFileSystem();
        UbiArtLayeredFileSystemOptions effectiveOptions = prioritizeDirectoryInput
            ? PrioritizeDirectoryInput(options)
            : options;
        UbiArtLayeredFileSystem layered = new(effectiveOptions, fileSystem);
        RegisterPatchIpkFirst(layered, effectiveOptions.InputPath, effectiveOptions.Platform, fileSystem);
        return layered;
    }

    private static UbiArtLayeredFileSystemOptions PrioritizeDirectoryInput(UbiArtLayeredFileSystemOptions options)
    {
        if (Path.GetExtension(options.InputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
            return options;

        return new()
        {
            InputPath = options.InputPath,
            Platform = options.Platform,
            IsUncooked = options.IsUncooked,
            AdditionalSearchRoots =
            [
                options.InputPath,
                .. options.AdditionalSearchRoots.Where(root =>
                    !string.Equals(root, options.InputPath, StringComparison.OrdinalIgnoreCase))
            ]
        };
    }

    private static void RegisterPatchIpkFirst(
        UbiArtLayeredFileSystem layered,
        string inputPath,
        UbiArtPlatform platform,
        IUbiArtFileSystem io)
    {
        string platformFolder = platform.GetCookedFolderName();
        if (string.IsNullOrWhiteSpace(platformFolder))
            return;

        string? parent = Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(inputPath)
            : Path.GetDirectoryName(io.GetFullPath(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (string.IsNullOrWhiteSpace(parent) || !io.DirectoryExists(parent))
            return;

        string expectedName = $"patch_{platformFolder}";
        foreach (string siblingIpk in io.GetFiles(parent, "*.ipk"))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(siblingIpk), expectedName, StringComparison.OrdinalIgnoreCase))
                continue;

            layered.RegisterIPK(siblingIpk);
            return;
        }
    }
}
