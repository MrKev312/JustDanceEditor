namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal readonly record struct LegacyPadding(int Length);

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
}

internal sealed class LegacyBinarySequence(IEnumerable<object?> fields)
{
    [LegacyBinaryField(0)]
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
