using KevInc.UbiArt.Ipk;

using Microsoft.Extensions.Logging;

using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

public static class UbiArtSecureFatWriter
{
    private const uint FileSignature = 0x55534654; // USFT
    private const uint FileVersion = 1;
    private const uint DefaultEngineSignature = 0x1F5EE42F;
    private static readonly string[] KnownPlatformSuffixes =
    [
        "pc",
        "wiiu",
        "wii",
        "nx",
        "x360",
        "durango",
        "scarlett",
        "ps3",
        "orbis",
        "prospero",
        "ggp"
    ];

    public static void Update(string archiveFolder, string platformFolder, ILogger logger)
    {
        string secureFatPath = Path.Combine(archiveFolder, "secure_fat.gf");
        ExistingSecureFat existing = TryReadExisting(secureFatPath);

        string platformSuffix = "_" + platformFolder;
        List<UbiArtIpkArchiveIndex> archives = Directory
            .EnumerateFiles(archiveFolder, "*.ipk", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileNameWithoutExtension(path)
                .EndsWith(platformSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                try
                {
                    return UbiArtIpkArchiveIndex.Read(path);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skipping unreadable IPK archive '{ArchivePath}' while rebuilding secure_fat.gf.", path);
                    return null;
                }
            })
            .OfType<UbiArtIpkArchiveIndex>()
            .Where(archive => !archive.IsPatch)
            .OrderBy(archive => archive.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (archives.Count == 0)
        {
            logger.LogWarning("No non-patch IPKs were found in '{ArchiveFolder}'; secure_fat.gf was not updated.", archiveFolder);
            return;
        }

        List<BundleEntry> bundles = AssignBundleIds(archives, existing.BundleIds, platformFolder);
        uint engineSignature = existing.EngineSignature
            ?? archives.GroupBy(archive => archive.EngineSignature)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .FirstOrDefault(DefaultEngineSignature);

        List<FileIdEntry> fileIdEntries = BuildFileIdEntries(bundles, existing.FileIds);
        int fileIdCount = fileIdEntries.Count;
        using FileStream stream = new(secureFatPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(stream, Encoding.UTF8);

        writer.WriteInt32BigEndian(unchecked((int)FileSignature));
        writer.WriteInt32BigEndian(unchecked((int)engineSignature));
        writer.WriteInt32BigEndian(unchecked((int)FileVersion));
        writer.WriteInt32BigEndian(fileIdCount);

        foreach (FileIdEntry entry in fileIdEntries)
        {
            writer.WriteInt32BigEndian(unchecked((int)entry.FileId));
            writer.WriteInt32BigEndian(entry.BundleIds.Count);
            foreach (byte bundleId in entry.BundleIds)
                writer.Write(bundleId);
        }

        List<BundleEntry> orderedBundles = OrderBundlesForWriting(bundles, existing.BundleNames);
        writer.WriteInt32BigEndian(orderedBundles.Count);
        foreach (BundleEntry bundle in orderedBundles)
        {
            writer.Write(bundle.Id);
            writer.WriteNTString(bundle.Name);
        }

        logger.LogInformation("Updated secure_fat.gf with {EntryCount} file id(s) across {BundleCount} bundle(s).", fileIdCount, bundles.Count);
    }

    private static List<FileIdEntry> BuildFileIdEntries(
        IReadOnlyCollection<BundleEntry> bundles,
        IReadOnlyList<FileIdEntry> existingEntries)
    {
        Dictionary<uint, List<byte>> ownersByFileId = [];
        foreach (BundleEntry bundle in bundles)
        {
            foreach (uint fileId in bundle.Archive.FileIds)
            {
                if (!ownersByFileId.TryGetValue(fileId, out List<byte>? owners))
                {
                    owners = [];
                    ownersByFileId[fileId] = owners;
                }

                if (!owners.Contains(bundle.Id))
                    owners.Add(bundle.Id);
            }
        }

        List<FileIdEntry> entries = [];
        HashSet<uint> handled = [];

        foreach (FileIdEntry existingEntry in existingEntries)
        {
            if (!ownersByFileId.TryGetValue(existingEntry.FileId, out List<byte>? currentOwners))
                continue;

            List<byte> orderedOwners = [];
            foreach (byte existingOwner in existingEntry.BundleIds)
            {
                if (currentOwners.Contains(existingOwner) && !orderedOwners.Contains(existingOwner))
                    orderedOwners.Add(existingOwner);
            }

            foreach (byte currentOwner in currentOwners)
            {
                if (!orderedOwners.Contains(currentOwner))
                    orderedOwners.Add(currentOwner);
            }

            if (orderedOwners.Count > 0)
            {
                entries.Add(new FileIdEntry(existingEntry.FileId, orderedOwners));
                handled.Add(existingEntry.FileId);
            }
        }

        foreach ((uint fileId, List<byte> owners) in ownersByFileId)
        {
            if (!handled.Contains(fileId))
                entries.Add(new FileIdEntry(fileId, owners));
        }

        return entries;
    }

    private static List<BundleEntry> OrderBundlesForWriting(
        IReadOnlyCollection<BundleEntry> bundles,
        IReadOnlyList<string> existingBundleNames)
    {
        List<BundleEntry> ordered = [];
        foreach (string existingName in existingBundleNames)
        {
            BundleEntry? bundle = bundles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, existingName, StringComparison.OrdinalIgnoreCase));
            if (bundle is not null && !ordered.Any(candidate => string.Equals(candidate.Name, bundle.Name, StringComparison.OrdinalIgnoreCase)))
                ordered.Add(bundle);
        }

        ordered.AddRange(bundles
            .Where(bundle => !ordered.Any(candidate => string.Equals(candidate.Name, bundle.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(bundle => bundle.Name, StringComparer.OrdinalIgnoreCase));

        return ordered;
    }

    private static List<BundleEntry> AssignBundleIds(
        IReadOnlyCollection<UbiArtIpkArchiveIndex> archives,
        IReadOnlyDictionary<string, byte> existingBundleIds,
        string platformFolder)
    {
        List<BundleEntry> bundles = [];
        HashSet<byte> usedIds = [];
        List<(UbiArtIpkArchiveIndex Archive, string Name)> namedArchives = archives
            .Select(archive => (Archive: archive, Name: GetBundleName(archive.FileName, platformFolder)))
            .ToList();

        foreach ((UbiArtIpkArchiveIndex archive, string bundleName) in namedArchives)
        {
            if (!existingBundleIds.TryGetValue(bundleName, out byte id) || usedIds.Contains(id))
                continue;

            usedIds.Add(id);
            bundles.Add(new BundleEntry(archive, bundleName, id));
        }

        foreach ((UbiArtIpkArchiveIndex archive, string bundleName) in namedArchives)
        {
            if (bundles.Any(bundle => string.Equals(bundle.Archive.Path, archive.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            byte id = GetNextBundleId(usedIds);
            usedIds.Add(id);
            bundles.Add(new BundleEntry(archive, bundleName, id));
        }

        return bundles;
    }

    private static byte GetNextBundleId(IReadOnlySet<byte> usedIds)
    {
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            byte value = checked((byte)id);
            if (!usedIds.Contains(value))
                return value;
        }

        throw new InvalidDataException("secure_fat.gf supports at most 256 IPK bundles.");
    }

    private static string GetBundleName(string fileName, string platformFolder)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        string[] suffixes = [platformFolder, .. KnownPlatformSuffixes];

        foreach (string suffix in suffixes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string marker = "_" + suffix.ToLowerInvariant();
            if (name.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                return name[..^marker.Length];
        }

        return name;
    }

    private static ExistingSecureFat TryReadExisting(string path)
    {
        if (!File.Exists(path))
            return ExistingSecureFat.Empty;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new(stream, Encoding.UTF8);

            uint signature = unchecked((uint)reader.ReadInt32BigEndian());
            if (signature != FileSignature)
                return ExistingSecureFat.Empty;

            uint engineSignature = unchecked((uint)reader.ReadInt32BigEndian());
            uint version = unchecked((uint)reader.ReadInt32BigEndian());
            if (version != FileVersion)
                return new ExistingSecureFat(
                    engineSignature,
                    new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase),
                    [],
                    []);

            uint fileCount = unchecked((uint)reader.ReadInt32BigEndian());
            List<FileIdEntry> fileIds = new(checked((int)fileCount));
            for (uint index = 0; index < fileCount; index++)
            {
                uint fileId = unchecked((uint)reader.ReadInt32BigEndian());
                int bundleCount = reader.ReadInt32BigEndian();
                if (bundleCount < 0)
                    return new ExistingSecureFat(
                        engineSignature,
                        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase),
                        [],
                        []);

                List<byte> ownerBundleIds = new(bundleCount);
                for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
                    ownerBundleIds.Add(reader.ReadByte());

                fileIds.Add(new FileIdEntry(fileId, ownerBundleIds));
            }

            int footerBundleCount = reader.ReadInt32BigEndian();
            if (footerBundleCount < 0)
                return new ExistingSecureFat(
                    engineSignature,
                    new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase),
                    fileIds,
                    []);

            Dictionary<string, byte> bundleIds = new(StringComparer.OrdinalIgnoreCase);
            List<string> bundleNames = new(footerBundleCount);
            for (int index = 0; index < footerBundleCount; index++)
            {
                byte id = reader.ReadByte();
                string name = reader.ReadNTString();
                bundleIds[name] = id;
                bundleNames.Add(name);
            }

            return new ExistingSecureFat(engineSignature, bundleIds, fileIds, bundleNames);
        }
        catch
        {
            return ExistingSecureFat.Empty;
        }
    }

    private sealed record BundleEntry(UbiArtIpkArchiveIndex Archive, string Name, byte Id);

    private sealed record FileIdEntry(uint FileId, IReadOnlyList<byte> BundleIds);

    private sealed record ExistingSecureFat(
        uint? EngineSignature,
        IReadOnlyDictionary<string, byte> BundleIds,
        IReadOnlyList<FileIdEntry> FileIds,
        IReadOnlyList<string> BundleNames)
    {
        public static ExistingSecureFat Empty { get; } = new(
            null,
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase),
            [],
            []);
    }
}