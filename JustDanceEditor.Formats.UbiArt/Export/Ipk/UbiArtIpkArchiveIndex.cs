using KevInc.UbiArt.Ipk;

using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal sealed partial class UbiArtIpkArchiveIndex
{
    private static readonly byte[] Magic = [0x50, 0xEC, 0x12, 0xBA];

    private UbiArtIpkArchiveIndex(
        string path,
        uint version,
        uint platformSupported,
        uint compressed,
        uint binaryScene,
        uint binaryLogic,
        uint dataSignature,
        uint engineSignature,
        uint engineVersion,
        bool swapPathAndName,
        IReadOnlyList<uint> fileIds,
        IReadOnlySet<string> entries,
        string? dominantMapName)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        NameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(path);
        Version = version;
        PlatformSupported = platformSupported;
        Compressed = compressed;
        BinaryScene = binaryScene;
        BinaryLogic = binaryLogic;
        DataSignature = dataSignature;
        EngineSignature = engineSignature;
        EngineVersion = engineVersion;
        SwapPathAndName = swapPathAndName;
        FileIds = fileIds;
        Entries = entries;
        DominantMapName = dominantMapName;
    }

    public string Path { get; }
    public string FileName { get; }
    public string NameWithoutExtension { get; }
    public uint Version { get; }
    public uint PlatformSupported { get; }
    public uint Compressed { get; }
    public uint BinaryScene { get; }
    public uint BinaryLogic { get; }
    public uint DataSignature { get; }
    public uint EngineSignature { get; }
    public uint EngineVersion { get; }
    public bool SwapPathAndName { get; }
    public IReadOnlyList<uint> FileIds { get; }
    public IReadOnlySet<string> Entries { get; }
    public string? DominantMapName { get; }

    public bool IsPatch => NameWithoutExtension.StartsWith("patch_", StringComparison.OrdinalIgnoreCase);

    public bool IsNumberedBundle => NumberedBundleNameRegex().IsMatch(NameWithoutExtension);

    public bool IsSharedBundle
    {
        get
        {
            string name = NameWithoutExtension;
            return (name.StartsWith("bundle_", StringComparison.OrdinalIgnoreCase) && !IsNumberedBundle)
                || name.Equals("bundle", StringComparison.OrdinalIgnoreCase)
                || name.Contains("bundlelogic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("logicbundle", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("bootbundle", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("blockflows", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsSongArchive => !IsPatch && !IsSharedBundle && !IsNumberedBundle;

    public static UbiArtIpkArchiveIndex Read(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        byte[] magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException($"'{path}' is not a valid UbiArt IPK file.");

        uint version = unchecked((uint)reader.ReadInt32BigEndian());
        uint platformSupported = unchecked((uint)reader.ReadInt32BigEndian());
        SkipInt32(reader);
        int filesCount = reader.ReadInt32BigEndian();
        uint compressed = unchecked((uint)reader.ReadInt32BigEndian());
        uint binaryScene = unchecked((uint)reader.ReadInt32BigEndian());
        uint binaryLogic = unchecked((uint)reader.ReadInt32BigEndian());
        uint dataSignature = unchecked((uint)reader.ReadInt32BigEndian());
        uint engineSignature = unchecked((uint)reader.ReadInt32BigEndian());
        uint engineVersion = unchecked((uint)reader.ReadInt32BigEndian());
        SkipInt32(reader);

        stream.Seek(0x30, SeekOrigin.Begin);

        List<RawEntry> rawEntries = new(filesCount);
        for (int index = 0; index < filesCount; index++)
            rawEntries.Add(ReadFileEntry(reader));

        bool swapPathAndName = rawEntries.Count > 0 &&
            rawEntries.Count(entry => entry.Name.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase)) > rawEntries.Count / 2;

        HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> mapCounts = new(StringComparer.OrdinalIgnoreCase);

        foreach (RawEntry rawEntry in rawEntries)
        {
            string logical = ToLogicalPath(rawEntry, swapPathAndName);
            entries.Add(logical);

            string? mapName = TryGetMapName(logical);
            if (mapName is not null)
                mapCounts[mapName] = mapCounts.GetValueOrDefault(mapName) + 1;
        }

        string? dominantMapName = mapCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault().Key;

        return new UbiArtIpkArchiveIndex(
            path,
            version,
            platformSupported,
            compressed,
            binaryScene,
            binaryLogic,
            dataSignature,
            engineSignature,
            engineVersion,
            swapPathAndName,
            rawEntries.Select(entry => entry.Crc).ToList(),
            entries,
            dominantMapName);
    }

    public bool Contains(string relativePath) => Entries.Contains(NormalizePath(relativePath));

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized.TrimStart('/').TrimEnd('/');
    }

    public static string? TryGetMapName(string relativePath)
    {
        Match match = MapPathRegex().Match(NormalizePath(relativePath));

        return match.Success ? match.Groups[1].Value : null;
    }

    public static string ToSongPattern(string relativePath, string mapName)
    {
        string normalized = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(mapName))
            return normalized;

        string escaped = Regex.Escape(mapName);
        normalized = Regex.Replace(
            normalized,
            $@"(?<=/world/maps/){escaped}(?=/)",
            "{map}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(
            normalized,
            escaped,
            "{map}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static RawEntry ReadFileEntry(BinaryReader reader)
    {
        int dummy1 = reader.ReadInt32BigEndian();
        SkipInt32(reader);
        SkipInt32(reader);
        SkipInt64(reader);
        SkipInt64(reader);

        if (dummy1 == 2)
        {
            SkipInt32(reader);
            SkipInt32(reader);
        }

        string path = reader.ReadNTString();
        string name = reader.ReadNTString();
        uint crc = unchecked((uint)reader.ReadInt32BigEndian());
        SkipInt32(reader);

        return new RawEntry(path, name, crc);
    }

    private static void SkipInt32(BinaryReader reader)
        => reader.BaseStream.Seek(sizeof(int), SeekOrigin.Current);

    private static void SkipInt64(BinaryReader reader)
        => reader.BaseStream.Seek(sizeof(long), SeekOrigin.Current);

    private static string ToLogicalPath(RawEntry entry, bool swapPathAndName)
    {
        (string fileName, string folderPath) = swapPathAndName
            ? (entry.Path, entry.Name)
            : (entry.Name.Contains('.', StringComparison.Ordinal) ? (entry.Name, entry.Path) : (entry.Path, entry.Name));

        string combined = string.IsNullOrEmpty(folderPath)
            ? fileName
            : $"{folderPath.TrimEnd('/', '\\')}/{fileName}";

        return NormalizePath(combined);
    }

    private sealed record RawEntry(string Path, string Name, uint Crc);

    [GeneratedRegex(@"^bundle_\d+_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedBundleNameRegex();

    [GeneratedRegex(@"(?:^|/)world/maps/([^/]+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MapPathRegex();
}
