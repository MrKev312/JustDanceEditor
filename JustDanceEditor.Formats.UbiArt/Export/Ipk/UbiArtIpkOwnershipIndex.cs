using KevInc.UbiArt.Ipk;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal static class UbiArtIpkOwnershipIndex
{
    public static IEnumerable<UbiArtIpkArchiveIndex> SelectEffectiveExactOwners(
        IEnumerable<UbiArtIpkArchiveIndex> exactOwners,
        string normalizedPath,
        string mapNameLower)
    {
        List<UbiArtIpkArchiveIndex> owners = [.. exactOwners.DistinctBy(archive => archive.Path, StringComparer.OrdinalIgnoreCase)];
        List<UbiArtIpkArchiveIndex> patchOwners = [.. owners.Where(archive => archive.IsPatch)];
        if (patchOwners.Count > 0 || owners.Count <= 1)
            return patchOwners.Count > 0 ? patchOwners : owners;

        string? mapName = UbiArtIpkArchiveIndex.TryGetMapName(normalizedPath);
        if (mapName is not null)
            return [SelectDominantPatternOwner(owners, UbiArtIpkArchiveIndex.ToSongPattern(normalizedPath, mapName))];

        if (!string.IsNullOrWhiteSpace(mapNameLower) &&
            normalizedPath.Contains($"/{mapNameLower}/", StringComparison.OrdinalIgnoreCase))
        {
            return [SelectDominantPatternOwner(owners, UbiArtIpkArchiveIndex.ToSongPattern(normalizedPath, mapNameLower))];
        }

        return owners;
    }

    public static Dictionary<string, List<UbiArtIpkArchiveIndex>> BuildExactOwners(IEnumerable<UbiArtIpkArchiveIndex> archives)
    {
        Dictionary<string, List<UbiArtIpkArchiveIndex>> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (UbiArtIpkArchiveIndex archive in archives)
        {
            foreach (string entry in archive.Entries)
            {
                if (!owners.TryGetValue(entry, out List<UbiArtIpkArchiveIndex>? archiveOwners))
                    owners[entry] = archiveOwners = [];
                archiveOwners.Add(archive);
            }
        }

        return owners;
    }

    public static Dictionary<string, List<UbiArtIpkArchiveIndex>> BuildPatternOwners(IEnumerable<UbiArtIpkArchiveIndex> archives)
    {
        Dictionary<string, List<UbiArtIpkArchiveIndex>> owners = new(StringComparer.OrdinalIgnoreCase);
        foreach (UbiArtIpkArchiveIndex archive in archives)
        {
            foreach (string entry in archive.Entries)
            {
                string? mapName = UbiArtIpkArchiveIndex.TryGetMapName(entry);
                if (mapName is null)
                    continue;

                string pattern = UbiArtIpkArchiveIndex.ToSongPattern(entry, mapName);
                if (!owners.TryGetValue(pattern, out List<UbiArtIpkArchiveIndex>? archiveOwners))
                    owners[pattern] = archiveOwners = [];

                if (!archiveOwners.Any(owner => string.Equals(owner.Path, archive.Path, StringComparison.OrdinalIgnoreCase)))
                    archiveOwners.Add(archive);
            }
        }

        return owners;
    }

    public static UbiArtIpkArchiveIndex SelectDominantPatternOwner(
        IReadOnlyCollection<UbiArtIpkArchiveIndex> owners,
        string pattern) => owners
            .OrderByDescending(archive => CountPatternEntries(archive, pattern))
            .ThenBy(archive => archive.IsPatch)
            .ThenBy(archive => archive.FileName, StringComparer.OrdinalIgnoreCase)
            .First();

    private static int CountPatternEntries(UbiArtIpkArchiveIndex archive, string pattern)
    {
        int count = 0;
        foreach (string entry in archive.Entries)
        {
            string? mapName = UbiArtIpkArchiveIndex.TryGetMapName(entry);
            if (mapName is not null &&
                string.Equals(UbiArtIpkArchiveIndex.ToSongPattern(entry, mapName), pattern, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }
}
