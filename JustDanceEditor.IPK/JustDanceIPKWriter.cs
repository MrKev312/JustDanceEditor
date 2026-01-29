using System.Text;

namespace JustDanceEditor.IPK;

public class JustDanceIPKWriter(string inputDirectory, string outputPath)
{
    private readonly byte[] IdString = [0x50, 0xEC, 0x12, 0xBA];

    // Hardcoded config matching the 2022 defaults for now
    private const int Version = 5;
    private const int GameId = 490359856;
    private const int EngineVersion = 253653;

    // Extensions to compress
    private readonly string[] _compressExtensions =
    [
        ".dtape.ckd", ".fx.fxb", ".m3d.ckd", ".png.ckd", ".tga.ckd"
    ];

    public void Pack()
    {
        string[] files = Directory.GetFiles(inputDirectory, "*", SearchOption.AllDirectories);
        List<WriterFileEntry> entries = [];
        long currentOffset = 0;

        // Memory stream to hold the actual file data blob until we write it
        // Note: For huge archives, you might want to write to a temp file instead of MemoryStream
        using MemoryStream dataBlob = new();

        Console.WriteLine($"Scanning {inputDirectory}...");

        foreach (string fullPath in files)
        {
            string fileName = Path.GetFileName(fullPath);
            // Get relative path
            string relativePath = Path.GetRelativePath(inputDirectory, Path.GetDirectoryName(fullPath)!);

            // Normalize path separators to forward slashes
            relativePath = relativePath.Replace("\\", "/");
            if (relativePath == ".")
                relativePath = "";
            if (!string.IsNullOrEmpty(relativePath) && !relativePath.EndsWith('/'))
                relativePath += "/";

            byte[] fileBytes = File.ReadAllBytes(fullPath);
            byte[] processedBytes = fileBytes;
            int zSize = 0;
            int size = fileBytes.Length;

            // Check compression
            bool shouldCompress = _compressExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

            if (shouldCompress)
            {
                Console.Write($"Compressing: {fileName}          \r");
                processedBytes = Compressor.Compress(fileBytes);
                zSize = processedBytes.Length;
            }

            // Flags logic
            int flags = fileName.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

            // Timestamp
            long timestamp = new DateTimeOffset(File.GetLastWriteTime(fullPath)).ToUnixTimeSeconds();

            // Calculate CRC (Path + Filename, Uppercase)
            string crcString = (relativePath + fileName).ToUpperInvariant();
            uint crc = UbiArtCRC.Compute(Encoding.ASCII.GetBytes(crcString));

            entries.Add(new WriterFileEntry
            {
                Name = fileName,
                Path = relativePath,
                Size = size,
                ZSize = zSize,
                TimeStamp = timestamp,
                Offset = currentOffset,
                Crc = (int)crc,
                Flags = flags
            });

            // Add to blob
            dataBlob.Write(processedBytes);
            currentOffset += processedBytes.Length;
        }

        Console.WriteLine($"\nWriting IPK to {outputPath}...");

        using FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(fs);

        // --- Write Header ---
        writer.Write(IdString);
        writer.WriteInt32BigEndian(Version);
        writer.WriteInt32BigEndian(8); // PlatformSupported

        // Calculate Base Offset
        // Header (48 bytes) + Entries (variable)
        long headerBaseSize = 48;
        long entriesSize = 0;

        foreach (WriterFileEntry entry in entries)
        {
            entriesSize += 4; // Dummy1
            entriesSize += 4; // Size
            entriesSize += 4; // ZSize
            entriesSize += 8; // TimeStamp
            entriesSize += 8; // Offset
            entriesSize += 4 + Encoding.UTF8.GetByteCount(entry.Name); // Name string
            entriesSize += 4 + Encoding.UTF8.GetByteCount(entry.Path); // Path string
            entriesSize += 4; // CRC
            entriesSize += 4; // Dummy2 (Flags)
        }

        long baseOffset = headerBaseSize + entriesSize;

        writer.WriteInt32BigEndian((int)baseOffset);
        writer.WriteInt32BigEndian(entries.Count);
        writer.WriteInt32BigEndian(0); // Compressed
        writer.WriteInt32BigEndian(0); // BinaryScene
        writer.WriteInt32BigEndian(0); // BinaryLogic
        writer.WriteInt32BigEndian(0); // DataSignature
        writer.WriteInt32BigEndian(GameId); // EngineSignature
        writer.WriteInt32BigEndian(EngineVersion); // EngineVersion
        writer.WriteInt32BigEndian(entries.Count); // Num_Files2

        // --- Write Entries ---
        foreach (WriterFileEntry entry in entries)
        {
            writer.WriteInt32BigEndian(1); // Dummy1 / NumOffset
            writer.WriteInt32BigEndian(entry.Size);
            writer.WriteInt32BigEndian(entry.ZSize);
            writer.WriteInt64BigEndian(entry.TimeStamp);
            writer.WriteInt64BigEndian(entry.Offset);
            writer.WriteNTString(entry.Name);
            writer.WriteNTString(entry.Path);
            writer.WriteInt32BigEndian(entry.Crc);
            writer.WriteInt32BigEndian(entry.Flags);
        }

        // --- Write Data ---
        dataBlob.Position = 0;
        dataBlob.CopyTo(fs);

        Console.WriteLine("Done.");
    }

    private class WriterFileEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int Size { get; set; }
        public int ZSize { get; set; } // 0 if not compressed
        public long TimeStamp { get; set; }
        public long Offset { get; set; }
        public int Crc { get; set; }
        public int Flags { get; set; }
    }
}