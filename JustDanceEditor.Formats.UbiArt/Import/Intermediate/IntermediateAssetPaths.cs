using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class IntermediateAssetPaths
{
    private static ImageEncoder WebpEncoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;

    public static string Resolve(string packageRoot, string relative) =>
        IntermediatePackageLayout.Resolve(packageRoot, relative);

    public static string EnsureFolder(string packageRoot, string relativeFolder, IFileSystem io)
    {
        string path = Resolve(packageRoot, relativeFolder);
        io.CreateDirectory(path);
        return path;
    }

    public static void ResetAssetsRoot(string packageRoot, IFileSystem io)
    {
        string assetsRoot = Resolve(packageRoot, IntermediatePackageLayout.Assets.Root);
        if (io.DirectoryExists(assetsRoot))
            io.DeleteDirectory(assetsRoot, true);
        io.CreateDirectory(assetsRoot);
    }

    public static void SaveAsWebp(Image<Bgra32> image, string destination, IFileSystem io)
    {
        io.CreateDirectory(Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException($"Could not determine the directory for '{destination}'."));
        image.Save(destination, WebpEncoder);
    }

    public static bool TryResolveReferencedCookedFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        out CookedFile? file)
    {
        file = null;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string normalized = relativePath.Replace('\\', '/');
        string[] candidates =
        [
            normalized,
            normalized.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + ".ckd"
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (fileSystem.GetFilePath(candidate, out file))
                return true;
        }

        return false;
    }

    public static void TryDeleteFile(string? path, IFileSystem io)
    {
        if (string.IsNullOrWhiteSpace(path) || !io.FileExists(path))
            return;

        try
        {
            io.DeleteFile(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void TryDeleteDirectory(string? path, ILogger logger, IFileSystem io)
    {
        if (string.IsNullOrWhiteSpace(path) || !io.DirectoryExists(path))
            return;

        try
        {
            io.DeleteDirectory(path, true);
        }
        catch (IOException ex)
        {
            logger.LogWarning("Failed to delete directory '{Path}': {Message}", path, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning("Failed to delete directory '{Path}': {Message}", path, ex.Message);
        }
    }
}
