namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal readonly record struct LegacyPadding(int Length);

internal readonly record struct LegacyBinaryTypeId<TMarker>;

internal readonly record struct LegacyUbiArtPath(string FileName, string Folder, uint? ResourceId = null)
{
    public static LegacyUbiArtPath FromFullPath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        string folder = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;

        if (!string.IsNullOrEmpty(folder) && !folder.EndsWith('/'))
            folder += "/";

        if (folder.StartsWith('/'))
            folder = folder[1..];

        return new LegacyUbiArtPath(fileName, folder);
    }

    public string FullPath => $"{Folder}{FileName}";
}

internal readonly record struct LegacyUbiArtFolderFirstPath(string Folder, string FileName, uint? ResourceId = null)
{
    public string FullPath => $"{Folder}{FileName}";
}

internal readonly record struct LegacyUbiArtFlexiblePath(string First, string Second, uint? ResourceId = null)
{
    public string FullPath => LegacyUbiArtPathOrder.Resolve(First, Second);
}

internal static class LegacyUbiArtPathOrder
{
    public static string Resolve(string first, string second)
    {
        if (string.IsNullOrEmpty(first))
            return second;

        if (string.IsNullOrEmpty(second))
            return first;

        if (LooksLikeFolder(first) && !LooksLikeFolder(second))
            return first + second;

        if (LooksLikeFolder(second) && !LooksLikeFolder(first))
            return second + first;

        return first + second;
    }

    private static bool LooksLikeFolder(string value)
    {
        string normalized = value.Replace('\\', '/');
        if (normalized.EndsWith('/'))
            return true;

        string fileName = Path.GetFileName(normalized);
        return !string.IsNullOrEmpty(fileName) && !Path.HasExtension(fileName);
    }
}

internal sealed class LegacyBinarySequence(IEnumerable<object?> fields)
{
    public IReadOnlyList<object?> Fields { get; } = [.. fields];
}

internal static class LegacyBinary
{
    public static LegacyBinarySequence Sequence(params object?[] fields) => new(fields);
    public static LegacyPadding Padding(int length) => new(length);
    public static LegacyUbiArtPath Path(string fileName, string folder) => new(fileName, folder);
    public static LegacyUbiArtPath Path(string fileName, string folder, uint resourceId) => new(fileName, folder, resourceId);
    public static LegacyUbiArtPath Path(string fullPath) => LegacyUbiArtPath.FromFullPath(fullPath);
}