using JustDanceEditor.Formats.UbiArt.FileSystem;
using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicAtlasLoader
{
    public static CinematicAtlasContainer LoadDefault(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        if (TryGetMissingDlcAtlasContainerPath(fileSystem, out string? missingAtlasPath))
        {
            throw new FileNotFoundException(
                $"The selected UbiArt DLC package references atlas-backed textures, but its atlas container was not found. Expected '{missingAtlasPath}'. Keep the DLC IPK or extracted IPK folder together with atlascontainer.ckd, secure_fat.gf, sgscontainer.ckd, and dlcdescriptor.ckd.",
                missingAtlasPath);
        }

        List<AtlasContainerBytes> sources = [.. ReadDefaultAtlasContainers(fileSystem)];
        if (sources.Count == 0)
        {
            logger.LogDebug("Cinematic atlascontainer was not found; material geometry will use no-atlas fallback.");
            return CinematicAtlasContainer.Empty;
        }

        Dictionary<uint, CinematicAtlas> mergedAtlases = [];
        int loadedCount = 0;
        foreach (AtlasContainerBytes source in sources)
        {
            try
            {
                CinematicAtlasContainer container = CinematicAtlasReader.ReadContainer(source.Bytes);
                foreach ((uint atlasId, CinematicAtlas atlas) in container.Atlases)
                    mergedAtlases.TryAdd(atlasId, atlas);

                loadedCount++;
                logger.LogDebug(
                    "Loaded cinematic atlascontainer '{AtlasContainerPath}' with {AtlasCount} atlas record(s).",
                    source.SourcePath,
                    container.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Could not read cinematic atlascontainer '{AtlasContainerPath}'.",
                    source.SourcePath);
            }
        }

        if (mergedAtlases.Count == 0)
        {
            logger.LogDebug("No readable cinematic atlascontainer records were found; material geometry will use no-atlas fallback.");
            return CinematicAtlasContainer.Empty;
        }

        if (loadedCount > 1)
        {
            logger.LogDebug(
                "Merged {AtlasContainerCount} cinematic atlascontainer file(s) into {AtlasCount} atlas record(s).",
                loadedCount,
                mergedAtlases.Count);
        }

        return new CinematicAtlasContainer(mergedAtlases);
    }

    public static CinematicAtlasContainer Read(byte[] bytes) =>
        CinematicAtlasReader.ReadContainer(bytes);

    public static bool TryReadLooseAtlas(
        byte[] bytes,
        [NotNullWhen(true)] out CinematicAtlas? atlas)
        => CinematicAtlasReader.TryReadLoose(bytes, out atlas);

    internal static CinematicAtlas ReadSingleAtlas(byte[] bytes) =>
        CinematicAtlasReader.ReadSingle(bytes);

    private static IEnumerable<AtlasContainerBytes> ReadDefaultAtlasContainers(JustDanceUbiArtFileSystem fileSystem)
    {
        HashSet<string> seenSources = new(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in GetLayeredAtlasCandidates(fileSystem))
        {
            if (!fileSystem.GetFilePath(relativePath, out CookedFile? atlasFile))
                continue;

            string sourcePath = atlasFile.RelativePath;
            if (!seenSources.Add(sourcePath))
                continue;

            using Stream stream = fileSystem.GetFileStream(atlasFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            yield return new AtlasContainerBytes(memory.ToArray(), sourcePath);
        }

        foreach (string physicalPath in GetPhysicalAtlasCandidates(fileSystem))
        {
            if (!File.Exists(physicalPath))
                continue;

            string sourcePath = Path.GetFullPath(physicalPath);
            if (!seenSources.Add(sourcePath))
                continue;

            yield return new AtlasContainerBytes(File.ReadAllBytes(physicalPath), sourcePath);
        }
    }

    private static IEnumerable<string> GetLayeredAtlasCandidates(JustDanceUbiArtFileSystem fileSystem)
    {
        string platformFolder = GetPlatformFolder(fileSystem);
        yield return "atlascontainer";
        yield return "atlascontainer.ckd";
        yield return Path.Combine("cache", "itf_cooked", platformFolder, "atlascontainer.ckd");

        foreach (string dlcName in GetDlcAtlasContainerNames(fileSystem))
        {
            yield return Path.Combine(dlcName, "atlascontainer");
            yield return Path.Combine(dlcName, "atlascontainer.ckd");
            yield return Path.Combine("cache", "itf_cooked", platformFolder, dlcName, "atlascontainer.ckd");
        }
    }

    private static IEnumerable<string> GetPhysicalAtlasCandidates(JustDanceUbiArtFileSystem fileSystem)
    {
        string platformFolder = GetPlatformFolder(fileSystem);
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        string inputRoot = Path.GetFullPath(fileSystem.InputFolders.InputFolder);
        roots.Add(inputRoot);

        DirectoryInfo? inputDirectory = Directory.Exists(inputRoot)
            ? new DirectoryInfo(inputRoot)
            : new FileInfo(inputRoot).Directory;
        if (inputDirectory != null && inputDirectory.Exists)
            roots.Add(inputDirectory.FullName);

        DirectoryInfo? parentDirectory = inputDirectory?.Parent;
        if (parentDirectory != null && parentDirectory.Exists)
        {
            roots.Add(parentDirectory.FullName);
            foreach (DirectoryInfo sibling in parentDirectory.EnumerateDirectories("*Logic*", SearchOption.TopDirectoryOnly))
                roots.Add(sibling.FullName);

            if (inputDirectory != null)
            {
                string bundleLogicName = inputDirectory.Name
                    .Replace("Bundle_0", "BundleLogic", StringComparison.OrdinalIgnoreCase)
                    .Replace("Bundle", "BundleLogic", StringComparison.OrdinalIgnoreCase);
                string bundleLogicPath = Path.Combine(parentDirectory.FullName, bundleLogicName);
                if (Directory.Exists(bundleLogicPath))
                    roots.Add(bundleLogicPath);
            }
        }

        foreach (string root in roots)
        {
            yield return Path.Combine(root, "cache", "itf_cooked", platformFolder, "atlascontainer.ckd");
            yield return Path.Combine(root, "atlascontainer.ckd");

            foreach (string dlcName in GetDlcAtlasContainerNames(fileSystem))
            {
                yield return Path.Combine(root, dlcName, "cache", "itf_cooked", platformFolder, "atlascontainer.ckd");
                yield return Path.Combine(root, dlcName, "atlascontainer.ckd");
                yield return Path.Combine(root, "cache", "itf_cooked", platformFolder, dlcName, "atlascontainer.ckd");
            }
        }
    }

    private static bool TryGetMissingDlcAtlasContainerPath(
        JustDanceUbiArtFileSystem fileSystem,
        [NotNullWhen(true)] out string? missingAtlasPath)
    {
        missingAtlasPath = null;
        string inputRoot = Path.GetFullPath(fileSystem.InputFolders.InputFolder);
        string? packageDirectory = null;

        if (Path.GetExtension(inputRoot).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
        {
            string inputName = Path.GetFileNameWithoutExtension(inputRoot);
            DirectoryInfo? parent = new FileInfo(inputRoot).Directory;
            if (parent != null &&
                (IsDlcMainSceneName(inputName) ||
                 File.Exists(Path.Combine(parent.FullName, "dlcdescriptor.ckd"))))
            {
                packageDirectory = parent.FullName;
            }
        }
        else if (Directory.Exists(inputRoot))
        {
            string inputName = new DirectoryInfo(inputRoot).Name;
            DirectoryInfo? parent = new DirectoryInfo(inputRoot).Parent;
            if (File.Exists(Path.Combine(inputRoot, "dlcdescriptor.ckd")))
            {
                packageDirectory = inputRoot;
            }
            else if (parent != null && IsDlcMainSceneName(inputName))
            {
                packageDirectory = parent.FullName;
            }
        }

        if (packageDirectory == null)
            return false;

        string expectedAtlas = Path.Combine(packageDirectory, "atlascontainer.ckd");
        if (File.Exists(expectedAtlas))
            return false;

        missingAtlasPath = expectedAtlas;
        return true;
    }

    private static bool IsDlcMainSceneName(string name) =>
        name.Contains("dlc", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("_main_scene_", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetDlcAtlasContainerNames(JustDanceUbiArtFileSystem fileSystem)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        AddDlcAtlasContainerName(names, fileSystem.SongName);

        string inputRoot = fileSystem.InputFolders.InputFolder;
        string? inputName = Directory.Exists(inputRoot)
            ? new DirectoryInfo(inputRoot).Name
            : Path.GetFileNameWithoutExtension(inputRoot);
        AddDlcAtlasContainerName(names, inputName);

        foreach (string name in names)
            yield return name;
    }

    private static void AddDlcAtlasContainerName(HashSet<string> names, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        string normalized = CinematicPath.Normalize(name);
        if (!string.IsNullOrWhiteSpace(normalized))
            names.Add(normalized);
    }

    private static string GetPlatformFolder(JustDanceUbiArtFileSystem fileSystem) =>
        fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked
            ? "wiiu"
            : fileSystem.VersionProfile.Platform.GetCookedFolderName();

    private sealed record AtlasContainerBytes(byte[] Bytes, string SourcePath);
}
