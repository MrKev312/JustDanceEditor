namespace JustDanceEditor.Formats.JDI;

public readonly record struct MaterializedOutputPath(string Path, bool IsTemporary, bool WasRedirected);

public static class MaterializedOutputPathResolver
{
    public static MaterializedOutputPath Resolve(string requestedPath, string protectedSourcePath, string suggestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSourcePath);

        if (!PathsOverlap(requestedPath, protectedSourcePath))
            return new MaterializedOutputPath(requestedPath, IsTemporary: false, WasRedirected: false);

        string uniqueName = CreateUniqueFolderName(suggestedName);

        foreach (string candidate in EnumerateSafeCandidates(requestedPath, protectedSourcePath, uniqueName))
        {
            if (!PathsOverlap(candidate, protectedSourcePath))
                return new MaterializedOutputPath(candidate, IsTemporary: false, WasRedirected: true);
        }

        string temporaryPath = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "Materialized", uniqueName);
        return new MaterializedOutputPath(temporaryPath, IsTemporary: true, WasRedirected: true);
    }

    public static bool PathsOverlap(string firstPath, string secondPath)
    {
        string first = NormalizeFullPath(firstPath);
        string second = NormalizeFullPath(secondPath);

        return IsSameOrAncestor(first, second) || IsSameOrAncestor(second, first);
    }

    private static IEnumerable<string> EnumerateSafeCandidates(string requestedPath, string protectedSourcePath, string uniqueName)
    {
        string requestedFullPath = NormalizeFullPath(requestedPath);
        string? requestedParent = Path.GetDirectoryName(requestedFullPath);
        if (!string.IsNullOrWhiteSpace(requestedParent))
            yield return Path.Combine(requestedParent, uniqueName);

        string sourceFullPath = NormalizeFullPath(protectedSourcePath);
        string? sourceParent = Path.GetDirectoryName(sourceFullPath);
        if (!string.IsNullOrWhiteSpace(sourceParent))
            yield return Path.Combine(sourceParent, uniqueName);
    }

    private static bool IsSameOrAncestor(string candidateAncestor, string candidateDescendant)
    {
        if (string.Equals(candidateAncestor, candidateDescendant, PathStringComparison))
            return true;

        string ancestorWithSeparator = Path.EndsInDirectorySeparator(candidateAncestor)
            ? candidateAncestor
            : candidateAncestor + Path.DirectorySeparatorChar;

        return candidateDescendant.StartsWith(ancestorWithSeparator, PathStringComparison);
    }

    private static string NormalizeFullPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string CreateUniqueFolderName(string suggestedName) =>
        $"{SanitizePathSegment(suggestedName)}_jdi_{Guid.NewGuid():N}";

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "song";

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        string sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "song" : sanitized;
    }

    private static StringComparison PathStringComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}