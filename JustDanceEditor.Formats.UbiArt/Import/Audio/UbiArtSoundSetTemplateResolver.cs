using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.FileSystem;

using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Import.Audio;

internal static partial class UbiArtSoundSetTemplateResolver
{
    private static readonly string[] AudioExtensions = [".ogg", ".opus", ".wav", ".wem"];

    public static IReadOnlyList<string> GetAudioPathCandidates(JustDanceUbiArtFileSystem fileSystem, string soundSetPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(soundSetPath))
            return [];

        if (!Path.GetExtension(soundSetPath).Equals(".tpl", StringComparison.OrdinalIgnoreCase))
            return [soundSetPath];

        if (!fileSystem.GetFilePath(soundSetPath, out CookedFile? templateFile))
            return GetFallbackCandidates(soundSetPath);

        byte[] bytes;
        using (Stream stream = fileSystem.GetFileStream(templateFile))
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }

        string[] candidates = ExtractAudioPathCandidates(bytes, soundSetPath);
        return candidates.Length == 0
            ? GetFallbackCandidates(soundSetPath)
            : candidates;
    }

    internal static string[] ExtractAudioPathCandidates(ReadOnlySpan<byte> bytes, string soundSetPath)
    {
        List<(int Index, string Value)> strings = ExtractPrintableStrings(bytes);
        string soundSetFolder = NormalizePath(Path.GetDirectoryName(soundSetPath) ?? string.Empty);
        List<(int Index, string Folder)> folders = [];
        foreach ((int index, string value) in strings)
        {
            string? folder = TryNormalizeFolder(value);
            if (!string.IsNullOrWhiteSpace(folder))
                folders.Add((index, folder));
        }

        List<string> candidates = [];

        foreach ((int index, string value) in strings)
        {
            foreach (Match match in AudioPathRegex().Matches(value))
            {
                string audioPath = NormalizePath(match.Groups["path"].Value);
                if (HasDirectory(audioPath))
                {
                    AddUnique(candidates, audioPath);
                    continue;
                }

                string? nearestFolder = folders
                    .Where(folder => folder.Index <= index)
                    .OrderByDescending(folder => folder.Index)
                    .Select(folder => folder.Folder)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(nearestFolder))
                    AddUnique(candidates, CombineRelative(nearestFolder, audioPath));

                if (!string.IsNullOrWhiteSpace(soundSetFolder))
                    AddUnique(candidates, CombineRelative(soundSetFolder, audioPath));

                foreach ((_, string folder) in folders)
                    AddUnique(candidates, CombineRelative(folder, audioPath));
            }
        }

        return [.. candidates];
    }

    private static string[] GetFallbackCandidates(string soundSetPath)
    {
        string withAudioExtension = Path.ChangeExtension(soundSetPath, ".wav");
        string folder = Path.GetDirectoryName(soundSetPath) ?? string.Empty;
        string fileName = Path.GetFileName(withAudioExtension);
        string strippedSetPrefix = fileName.StartsWith("set_", StringComparison.OrdinalIgnoreCase)
            ? fileName["set_".Length..]
            : fileName;

        List<string> candidates = [];
        AddUnique(candidates, NormalizePath(withAudioExtension));
        if (!string.Equals(fileName, strippedSetPrefix, StringComparison.OrdinalIgnoreCase))
            AddUnique(candidates, CombineRelative(NormalizePath(folder), strippedSetPrefix));

        return [.. candidates];
    }

    private static List<(int Index, string Value)> ExtractPrintableStrings(ReadOnlySpan<byte> bytes)
    {
        List<(int Index, string Value)> strings = [];
        int start = -1;

        for (int i = 0; i <= bytes.Length; i++)
        {
            bool printable = i < bytes.Length && bytes[i] is >= 0x20 and <= 0x7E;
            if (printable)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0 && i - start >= 4)
                strings.Add((start, System.Text.Encoding.ASCII.GetString(bytes[start..i])));

            start = -1;
        }

        return strings;
    }

    private static string? TryNormalizeFolder(string value)
    {
        string normalized = NormalizePath(value);
        if (!normalized.Contains('/', StringComparison.Ordinal) || !normalized.EndsWith('/'))
            return null;

        return normalized;
    }

    private static bool HasDirectory(string path) =>
        path.Contains('/', StringComparison.Ordinal) ||
        path.Contains('\\', StringComparison.Ordinal);

    private static string CombineRelative(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return NormalizePath(fileName);

        return NormalizePath(folder.TrimEnd('/', '\\') + "/" + fileName.TrimStart('/', '\\'));
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/', '.');

    private static void AddUnique(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!AudioExtensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            values.Add(value);
    }

    [GeneratedRegex(@"(?<path>[A-Za-z0-9_./\\-]+?\.(?:ogg|opus|wav|wem))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioPathRegex();
}