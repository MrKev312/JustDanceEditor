using KevInc.UbiArt.Ipk;

using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal readonly record struct UbiArtIpkMergeFile(string FullPath, string RelativePath);

internal static class UbiArtIpkArchiveMerger
{
    private static readonly byte[] Magic = [0x50, 0xEC, 0x12, 0xBA];

    private static readonly string[] CompressExtensions =
    [
        ".dtape.ckd",
        ".fx.fxb",
        ".m3d.ckd",
        ".png.ckd",
        ".tga.ckd"
    ];

    public static void Merge(
        string existingArchivePath,
        string outputArchivePath,
        IReadOnlyCollection<UbiArtIpkMergeFile> stagedFiles,
        bool swapPathAndName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputArchivePath);
        ArgumentNullException.ThrowIfNull(stagedFiles);

        byte[] originalArchive = File.ReadAllBytes(existingArchivePath);
        ExistingArchive existingArchive = ReadExistingArchive(originalArchive);

        Dictionary<string, UbiArtIpkMergeFile> stagedByPath = stagedFiles
            .GroupBy(file => UbiArtIpkArchiveIndex.NormalizePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        List<MergeEntry> mergedEntries = new(existingArchive.Entries.Count + stagedByPath.Count);
        HashSet<string> consumed = new(StringComparer.OrdinalIgnoreCase);

        foreach (ExistingEntry existingEntry in existingArchive.Entries)
        {
            if (stagedByPath.TryGetValue(existingEntry.LogicalPath, out UbiArtIpkMergeFile stagedFile))
            {
                mergedEntries.Add(MergeEntry.FromStagedReplacement(existingEntry, stagedFile));
                consumed.Add(existingEntry.LogicalPath);
            }
            else
            {
                mergedEntries.Add(MergeEntry.FromExisting(existingEntry, originalArchive, existingArchive.BaseOffset));
            }
        }

        foreach ((string relativePath, UbiArtIpkMergeFile stagedFile) in stagedByPath
            .Where(pair => !consumed.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            mergedEntries.Add(MergeEntry.FromStagedAddition(relativePath, stagedFile, swapPathAndName));
        }

        WriteMergedArchive(existingArchive.Header, outputArchivePath, mergedEntries);
    }

    private static ExistingArchive ReadExistingArchive(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8);

        byte[] magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Input is not a valid UbiArt IPK archive.");

        byte[] header = new byte[0x30];
        Buffer.BlockCopy(bytes, 0, header, 0, header.Length);

        SkipInt32(reader);
        SkipInt32(reader);
        int baseOffset = reader.ReadInt32BigEndian();
        int filesCount = reader.ReadInt32BigEndian();

        stream.Seek(0x30, SeekOrigin.Begin);

        List<ExistingEntry> entries = new(filesCount);
        for (int index = 0; index < filesCount; index++)
            entries.Add(ReadEntry(reader));

        bool swapPathAndName = entries.Count > 0 &&
            entries.Count(entry => entry.SecondString.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase)) > entries.Count / 2;

        for (int index = 0; index < entries.Count; index++)
            entries[index] = entries[index] with { LogicalPath = ToLogicalPath(entries[index], swapPathAndName) };

        return new ExistingArchive(header, baseOffset, entries);
    }

    private static ExistingEntry ReadEntry(BinaryReader reader)
    {
        int dummy1 = reader.ReadInt32BigEndian();
        int size = reader.ReadInt32BigEndian();
        int zSize = reader.ReadInt32BigEndian();
        long timeStamp = reader.ReadInt64BigEndian();
        long offset = reader.ReadInt64BigEndian();
        int? extra1 = null;
        int? extra2 = null;

        if (dummy1 == 2)
        {
            extra1 = reader.ReadInt32BigEndian();
            extra2 = reader.ReadInt32BigEndian();
        }

        string firstString = reader.ReadNTString();
        string secondString = reader.ReadNTString();
        int crc = reader.ReadInt32BigEndian();
        int flags = reader.ReadInt32BigEndian();

        return new ExistingEntry(
            dummy1,
            size,
            zSize,
            timeStamp,
            offset,
            extra1,
            extra2,
            firstString,
            secondString,
            crc,
            flags,
            LogicalPath: string.Empty);
    }

    private static void SkipInt32(BinaryReader reader)
        => reader.BaseStream.Seek(sizeof(int), SeekOrigin.Current);

    private static string ToLogicalPath(ExistingEntry entry, bool swapPathAndName)
    {
        (string fileName, string folderPath) = swapPathAndName
            ? (entry.FirstString, entry.SecondString)
            : (entry.SecondString.Contains('.', StringComparison.Ordinal) ? (entry.SecondString, entry.FirstString) : (entry.FirstString, entry.SecondString));

        string combined = string.IsNullOrEmpty(folderPath)
            ? fileName
            : $"{folderPath.TrimEnd('/', '\\')}/{fileName}";

        return UbiArtIpkArchiveIndex.NormalizePath(combined);
    }

    private static void WriteMergedArchive(byte[] originalHeader, string outputArchivePath, IReadOnlyList<MergeEntry> entries)
    {
        long entriesSize = entries.Sum(entry => entry.MetadataSize);
        long baseOffset = 0x30L + entriesSize;
        if (baseOffset > int.MaxValue)
            throw new InvalidDataException($"IPK header is too large: {baseOffset} bytes.");

        long dataOffset = 0;
        foreach (MergeEntry entry in entries)
        {
            entry.Offset = dataOffset;
            dataOffset += entry.StoredBytes.Length;
        }

        using FileStream stream = new(outputArchivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(stream, Encoding.UTF8);

        writer.Write(originalHeader);
        WriteHeaderValue(writer, 0x0C, checked((uint)baseOffset));
        WriteHeaderValue(writer, 0x10, checked((uint)entries.Count));
        WriteHeaderValue(writer, 0x2C, checked((uint)entries.Count));

        foreach (MergeEntry entry in entries)
            entry.WriteMetadata(writer);

        foreach (MergeEntry entry in entries)
            writer.Write(entry.StoredBytes);
    }

    private static void WriteHeaderValue(BinaryWriter writer, long offset, uint value)
    {
        writer.BaseStream.Seek(offset, SeekOrigin.Begin);
        writer.WriteInt32BigEndian(unchecked((int)value));
        writer.BaseStream.Seek(0, SeekOrigin.End);
    }

    private static byte[] ReadStoredBytes(ExistingEntry entry, byte[] archiveBytes, int baseOffset)
    {
        long offset = baseOffset + entry.Offset;
        int storedSize = entry.ZSize == 0 ? entry.Size : entry.ZSize;
        byte[] storedBytes = new byte[storedSize];
        Buffer.BlockCopy(archiveBytes, checked((int)offset), storedBytes, 0, storedBytes.Length);
        return storedBytes;
    }

    private static StagedPayload ReadStagedPayload(UbiArtIpkMergeFile stagedFile)
    {
        byte[] bytes = File.ReadAllBytes(stagedFile.FullPath);
        int size = GetInt32Size(bytes.Length, stagedFile.FullPath);
        string fileName = Path.GetFileName(UbiArtIpkArchiveIndex.NormalizePath(stagedFile.RelativePath));

        if (!CompressExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return new StagedPayload(bytes, size, ZSize: 0);

        byte[] compressedBytes = Compressor.Compress(bytes);
        return new StagedPayload(compressedBytes, size, GetInt32Size(compressedBytes.Length, stagedFile.FullPath));
    }

    private static int GetInt32Size(long size, string path)
    {
        if (size > int.MaxValue)
            throw new InvalidDataException($"File is too large for the IPK format: {path} ({size} bytes).");

        return (int)size;
    }

    private static int ComputeCrc(string relativePath)
    {
        string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);
        uint crc = UbiArtCRC.Compute(Encoding.ASCII.GetBytes(normalized.ToUpperInvariant()));
        return unchecked((int)crc);
    }

    private static long GetTimestamp(string path)
        => new DateTimeOffset(File.GetLastWriteTime(path)).ToUnixTimeSeconds();

    private static int ComputeFlags(string relativePath)
        => Path.GetFileName(relativePath).EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    private sealed record ExistingArchive(byte[] Header, int BaseOffset, IReadOnlyList<ExistingEntry> Entries);

    private sealed record ExistingEntry(
        int Dummy1,
        int Size,
        int ZSize,
        long TimeStamp,
        long Offset,
        int? Extra1,
        int? Extra2,
        string FirstString,
        string SecondString,
        int Crc,
        int Flags,
        string LogicalPath);

    private sealed record StagedPayload(byte[] StoredBytes, int Size, int ZSize);

    private sealed class MergeEntry
    {
        private MergeEntry(
            int dummy1,
            int size,
            int zSize,
            long timeStamp,
            int? extra1,
            int? extra2,
            string firstString,
            string secondString,
            int crc,
            int flags,
            byte[] storedBytes)
        {
            Dummy1 = dummy1;
            Size = size;
            ZSize = zSize;
            TimeStamp = timeStamp;
            Extra1 = extra1;
            Extra2 = extra2;
            FirstString = firstString;
            SecondString = secondString;
            Crc = crc;
            Flags = flags;
            StoredBytes = storedBytes;
        }

        public int Dummy1 { get; }
        public int Size { get; }
        public int ZSize { get; }
        public long TimeStamp { get; }
        public long Offset { get; set; }
        public int? Extra1 { get; }
        public int? Extra2 { get; }
        public string FirstString { get; }
        public string SecondString { get; }
        public int Crc { get; }
        public int Flags { get; }
        public byte[] StoredBytes { get; }

        public long MetadataSize
        {
            get
            {
                long size = sizeof(int) * 3 + sizeof(long) * 2;
                if (Dummy1 == 2)
                    size += sizeof(int) * 2;

                size += sizeof(int) + Encoding.UTF8.GetByteCount(FirstString);
                size += sizeof(int) + Encoding.UTF8.GetByteCount(SecondString);
                size += sizeof(int) * 2;
                return size;
            }
        }

        public static MergeEntry FromExisting(ExistingEntry existingEntry, byte[] archiveBytes, int baseOffset)
            => new(
                existingEntry.Dummy1,
                existingEntry.Size,
                existingEntry.ZSize,
                existingEntry.TimeStamp,
                existingEntry.Extra1,
                existingEntry.Extra2,
                existingEntry.FirstString,
                existingEntry.SecondString,
                existingEntry.Crc,
                existingEntry.Flags,
                ReadStoredBytes(existingEntry, archiveBytes, baseOffset));

        public static MergeEntry FromStagedReplacement(ExistingEntry existingEntry, UbiArtIpkMergeFile stagedFile)
        {
            StagedPayload payload = ReadStagedPayload(stagedFile);
            return new MergeEntry(
                existingEntry.Dummy1,
                payload.Size,
                payload.ZSize,
                GetTimestamp(stagedFile.FullPath),
                existingEntry.Extra1,
                existingEntry.Extra2,
                existingEntry.FirstString,
                existingEntry.SecondString,
                existingEntry.Crc,
                ComputeFlags(stagedFile.RelativePath),
                payload.StoredBytes);
        }

        public static MergeEntry FromStagedAddition(string relativePath, UbiArtIpkMergeFile stagedFile, bool swapPathAndName)
        {
            StagedPayload payload = ReadStagedPayload(stagedFile);
            string normalized = UbiArtIpkArchiveIndex.NormalizePath(relativePath);
            string fileName = Path.GetFileName(normalized);
            string? folder = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder))
                folder += "/";
            else
                folder = string.Empty;

            string firstString = swapPathAndName ? fileName : folder;
            string secondString = swapPathAndName ? folder : fileName;
            int flags = ComputeFlags(normalized);

            return new MergeEntry(
                dummy1: 1,
                payload.Size,
                payload.ZSize,
                GetTimestamp(stagedFile.FullPath),
                extra1: null,
                extra2: null,
                firstString,
                secondString,
                ComputeCrc(normalized),
                flags,
                payload.StoredBytes);
        }

        public void WriteMetadata(BinaryWriter writer)
        {
            writer.WriteInt32BigEndian(Dummy1);
            writer.WriteInt32BigEndian(Size);
            writer.WriteInt32BigEndian(ZSize);
            writer.WriteInt64BigEndian(TimeStamp);
            writer.WriteInt64BigEndian(Offset);

            if (Dummy1 == 2)
            {
                writer.WriteInt32BigEndian(Extra1.GetValueOrDefault());
                writer.WriteInt32BigEndian(Extra2.GetValueOrDefault());
            }

            writer.WriteNTString(FirstString);
            writer.WriteNTString(SecondString);
            writer.WriteInt32BigEndian(Crc);
            writer.WriteInt32BigEndian(Flags);
        }
    }
}
