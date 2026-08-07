using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class MotionAssetImporter
{
    public static void Import(ConversionContext context, string packageRoot, IFileSystem io)
    {
        foreach (string motionFolder in EnumerateMotionSearchFolders(context))
            ImportMotionClassifiers(context, motionFolder, packageRoot);

        foreach (GestureFolderSource gestureFolder in EnumerateGestureSearchFolders(context))
        {
            CopyCookedFiles(
                context,
                gestureFolder.SourceRelativeFolder,
                "*.gesture",
                IntermediateAssetPaths.Resolve(packageRoot, gestureFolder.PackageRelativeFolder),
                io);
        }

        if (UbiArtGestureFolders.TryGetPlatformFolder(context.FileSystem.VersionProfile.Platform, out string? platformFolder) &&
            platformFolder != null)
        {
            CopyCookedFiles(
                context,
                context.FileSystem.InputFolders.TimelineFolder + "/gestures",
                "*.gesture",
                IntermediateAssetPaths.Resolve(packageRoot, UbiArtGestureFolders.PackageFolder(platformFolder)),
                io);
        }

        foreach (MotionClip clip in context.SongData?.Clips.OfType<MotionClip>() ?? [])
        {
            if (Path.GetExtension(clip.ClassifierPath).Equals(".gesture", StringComparison.OrdinalIgnoreCase))
            {
                string? packageFolder = GetReferencedGesturePackageFolder(context, clip.ClassifierPath);
                if (packageFolder != null)
                    CopyReferencedCookedFile(context, clip.ClassifierPath, IntermediateAssetPaths.Resolve(packageRoot, packageFolder), io);
            }
            else
            {
                ImportReferencedMotionClassifier(context, clip.ClassifierPath, packageRoot);
            }
        }
    }

    private static void ImportMotionClassifiers(ConversionContext context, string relativeFolder, string packageRoot)
    {
        CookedFile[] files;
        try
        {
            files = context.FileSystem.GetAllFiles(relativeFolder, "*.msm");
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        (CookedFile File, string Name)[] uniqueFiles = [.. files
            .Select(file => (File: file, Name: $"{file.Name}{file.Extension}"))
            .Where(item => seen.Add(item.Name))];
        Parallel.ForEach(uniqueFiles, item => ImportClassifier(context, item.File, item.Name, packageRoot));
    }

    private static void ImportReferencedMotionClassifier(ConversionContext context, string relativePath, string packageRoot)
    {
        if (!IntermediateAssetPaths.TryResolveReferencedCookedFile(context.FileSystem, relativePath, out CookedFile? file) || file == null)
            return;

        ImportClassifier(context, file, $"{file.Name}{file.Extension}", packageRoot);
    }

    private static void ImportClassifier(ConversionContext context, CookedFile file, string name, string packageRoot)
    {
        try
        {
            using Stream source = context.FileSystem.GetFileStream(file);
            using MemoryStream buffer = new();
            source.CopyTo(buffer);
            JdiMotionClassifierStorage.ImportClassifier(packageRoot, name, buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static IEnumerable<string> EnumerateMotionSearchFolders(ConversionContext context)
    {
        string movesFolder = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "moves");
        yield return context.FileSystem.InputFolders.MovesFolder;
        yield return movesFolder;
        foreach (UbiArtPlatform platform in Enum.GetValues<UbiArtPlatform>())
        {
            if (platform == UbiArtPlatform.Uncooked)
                continue;

            string platformFolder = platform.GetCookedFolderName();
            if (!string.IsNullOrWhiteSpace(platformFolder))
                yield return Path.Combine(movesFolder, platformFolder);
        }

        yield return Path.Combine(movesFolder, "wiiu");
    }

    private static IEnumerable<GestureFolderSource> EnumerateGestureSearchFolders(ConversionContext context)
    {
        string movesFolder = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "moves");
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string[] directories;
        try
        {
            directories = context.FileSystem.GetDirectories(movesFolder);
        }
        catch (DirectoryNotFoundException)
        {
            directories = [];
        }

        foreach (string directory in directories)
        {
            string platformFolder = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(platformFolder) && seen.Add(platformFolder))
                yield return new(Path.Combine(movesFolder, platformFolder), UbiArtGestureFolders.PackageFolder(platformFolder));
        }

        foreach (UbiArtGestureFolder gestureFolder in UbiArtGestureFolders.All)
        {
            if (seen.Add(gestureFolder.PlatformFolder))
                yield return new(Path.Combine(movesFolder, gestureFolder.PlatformFolder), gestureFolder.PackageRelativeFolder);
        }
    }

    private static string? GetReferencedGesturePackageFolder(ConversionContext context, string classifierPath)
    {
        if (TryGetGesturePackageFolderFromPath(classifierPath, out string? packageFolder))
            return packageFolder;

        return UbiArtGestureFolders.TryGetPlatformFolder(context.FileSystem.VersionProfile.Platform, out string? platformFolder) &&
            platformFolder != null
            ? UbiArtGestureFolders.PackageFolder(platformFolder)
            : null;
    }

    private static bool TryGetGesturePackageFolderFromPath(string relativePath, out string? packageFolder)
    {
        packageFolder = null;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        int movesIndex = Array.FindIndex(segments, segment => segment.Equals("moves", StringComparison.OrdinalIgnoreCase));
        if (movesIndex >= 0 && movesIndex + 1 < segments.Length)
        {
            packageFolder = UbiArtGestureFolders.PackageFolder(segments[movesIndex + 1]);
            return true;
        }

        foreach (UbiArtGestureFolder gestureFolder in UbiArtGestureFolders.All)
        {
            if (segments.Any(segment => segment.Equals(gestureFolder.PlatformFolder, StringComparison.OrdinalIgnoreCase)))
            {
                packageFolder = gestureFolder.PackageRelativeFolder;
                return true;
            }
        }

        return false;
    }

    private static void CopyCookedFiles(
        ConversionContext context,
        string relativeFolder,
        string pattern,
        string destinationFolder,
        IFileSystem io)
    {
        CookedFile[] files;
        try
        {
            files = context.FileSystem.GetAllFiles(relativeFolder, pattern);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        (CookedFile File, string Name)[] uniqueFiles = [.. files
            .Select(file => (File: file, Name: $"{file.Name}{file.Extension}"))
            .Where(item => seen.Add(item.Name))];
        if (uniqueFiles.Length == 0)
            return;

        io.CreateDirectory(destinationFolder);
        Parallel.ForEach(uniqueFiles, item => CopyCookedFile(context, item.File, io.Combine(destinationFolder, item.Name)));
    }

    private static void CopyReferencedCookedFile(ConversionContext context, string relativePath, string destinationFolder, IFileSystem io)
    {
        if (!IntermediateAssetPaths.TryResolveReferencedCookedFile(context.FileSystem, relativePath, out CookedFile? file) || file == null)
            return;

        io.CreateDirectory(destinationFolder);
        CopyCookedFile(context, file, io.Combine(destinationFolder, $"{file.Name}{file.Extension}"));
    }

    private static void CopyCookedFile(ConversionContext context, CookedFile file, string destination)
    {
        try
        {
            using Stream source = context.FileSystem.GetFileStream(file);
            using FileStream output = File.Open(destination, FileMode.Create, FileAccess.Write);
            source.CopyTo(output);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private readonly record struct GestureFolderSource(string SourceRelativeFolder, string PackageRelativeFolder);
}
