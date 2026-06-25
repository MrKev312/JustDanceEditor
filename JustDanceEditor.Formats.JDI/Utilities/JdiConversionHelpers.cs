using JustDanceEditor.Formats.JDI.Metadata;

namespace JustDanceEditor.Formats.JDI.Utilities;

public static class JdiConversionHelpers
{
    public static string BuildSongOutputFolder(string baseOutput, IntermediateMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseOutput);
        ArgumentNullException.ThrowIfNull(metadata);

        string? codeName = string.IsNullOrWhiteSpace(metadata.MapName) ? metadata.Title : metadata.MapName;
        if (string.IsNullOrWhiteSpace(codeName))
            codeName = "Song";

        string folderName = SanitizeFolderName(codeName);
        return Path.Combine(baseOutput, folderName);
    }

    public static string SanitizeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    public static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source intermediate folder '{sourceDir}' does not exist.");

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(directory);
            string target = Path.Combine(destinationDir, name);
            Directory.CreateDirectory(target);
            CopyDirectoryRecursive(directory, target);
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string target = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, target, true);
        }
    }

    public static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string targetSub = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, targetSub);
        }
    }

    public static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // Swallow cleanup errors; they are non-fatal.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}