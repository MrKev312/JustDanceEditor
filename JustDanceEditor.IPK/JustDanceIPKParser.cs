namespace JustDanceEditor.IPK;

public class JustDanceIPKParser
{
    readonly byte[] IdString = [0x50, 0xEC, 0x12, 0xBA];
    long version;
    long baseOffset;
    long filesCount;
    readonly Stream fileStream;

    readonly string outputDirectory;
    private bool pathsAreSwapped = false;

    public JustDanceIPKParser(Stream fileStream, string outputPath)
    {
        this.fileStream = fileStream;
        outputDirectory = outputPath;
    }

    public JustDanceIPKParser(string filePath, string outputPath)
    {
        // If the file doesn't exist, throw an exception
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        outputDirectory = outputPath;
    }

    public void Parse(bool ShowInfo = false)
    {
        using BinaryReader reader = new(fileStream);
        byte[] magic = reader.ReadBytes(4);
        // Check if the magic is correct
        if (!magic.SequenceEqual(IdString))
            throw new InvalidDataException("Invalid IPK file");

        version = reader.ReadInt32BigEndian();

        // Print the version
        if (ShowInfo)
            Console.WriteLine($"IPK version: {version}");

        // Skipping dummy long
        reader.ReadInt32BigEndian();
        baseOffset = reader.ReadInt32BigEndian();
        filesCount = reader.ReadInt32BigEndian();

        // Print the number of files
        if (ShowInfo)
            Console.WriteLine($"Number of files: {filesCount}");

        fileStream.Seek(0x30, SeekOrigin.Begin);

        List<FileEntry> entries = [];

        // First read all the file entries
        for (int i = 0; i < filesCount; i++)
            entries.Add(ReadFileEntry(reader));

        // Detect if paths are swapped by checking if most Name entries contain "cache/itf_cooked"
        // This is a marker for WiiU bundles where Name contains the full folder path and Path contains filename
        if (entries.Count > 0)
        {
            int cacheCount = entries.Count(e => e.Name.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase));
            pathsAreSwapped = cacheCount > entries.Count / 2;
        }

        // Now process the file entries
        foreach (FileEntry entry in entries)
        {
            fileStream.Seek(entry.Offset, SeekOrigin.Begin);
            ProcessFileEntry(entry, fileStream, outputDirectory, pathsAreSwapped, ShowInfo);
        }
    }

    public void ParseInfo()
    {
        using BinaryReader reader = new(fileStream);
        byte[] magic = reader.ReadBytes(4);
        // Check if the magic is correct
        if (!magic.SequenceEqual(IdString))
            throw new InvalidDataException("Invalid IPK file");

        version = reader.ReadInt32BigEndian();
        Console.WriteLine($"IPK version: {version}");

        // Skipping dummy long
        reader.ReadInt32BigEndian();
        baseOffset = reader.ReadInt32BigEndian();
        filesCount = reader.ReadInt32BigEndian();

        Console.WriteLine($"Base offset: {baseOffset}");
        Console.WriteLine($"Number of files: {filesCount}");

        fileStream.Seek(0x30, SeekOrigin.Begin);

        List<FileEntry> entries = [];

        // First read all the file entries
        for (int i = 0; i < filesCount; i++)
            entries.Add(ReadFileEntry(reader));

        // Detect if paths are swapped by checking if most Path entries contain "cache/itf_cooked"
        if (entries.Count > 0)
        {
            int cacheCount = entries.Count(e => e.Path.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase));
            pathsAreSwapped = cacheCount > entries.Count / 2;
            Console.WriteLine($"Paths are swapped: {pathsAreSwapped}");
        }

        // Print first 20 entries
        Console.WriteLine($"\nFirst {Math.Min(20, entries.Count)} file entries:");
        for (int i = 0; i < Math.Min(20, entries.Count); i++)
        {
            var entry = entries[i];
            var (fileName, folderPath) = pathsAreSwapped 
                ? (entry.Path, entry.Name)
                : (entry.Name.Contains('.') ? (entry.Name, entry.Path) : (entry.Path, entry.Name));
            Console.WriteLine($"  [{i}] Path: '{folderPath}', Name: '{fileName}', Size: {entry.Size}");
        }
    }

    private FileEntry ReadFileEntry(BinaryReader reader)
    {
        FileEntry entry = new()
        {
            Dummy1 = reader.ReadInt32BigEndian(),
            Size = reader.ReadInt32BigEndian(),
            ZSize = reader.ReadInt32BigEndian(),
            TimeStamp = reader.ReadInt64BigEndian(),
            Offset = reader.ReadInt64BigEndian()
        };

        if (entry.Dummy1 == 2)
        {
            // Skipping two dummy longs
            reader.ReadInt32BigEndian();
            reader.ReadInt32BigEndian();
        }

        entry.Path = reader.ReadNTString();
        entry.Name = reader.ReadNTString();
        entry.Crc = reader.ReadInt32BigEndian();
        entry.Dummy2 = reader.ReadInt32BigEndian();

        entry.Offset += baseOffset;
        return entry;
    }

    private static void ProcessFileEntry(FileEntry entry, Stream fileStream, string outputDirectory, bool pathsAreSwapped, bool ShowInfo = false)
    {
        // If paths are swapped (WiiU bundles), use swapped logic
        (string fileName, string folderPath) = pathsAreSwapped 
            ? (entry.Path, entry.Name)
            : (entry.Name.Contains('.') ? (entry.Name, entry.Path) : (entry.Path, entry.Name));
        BinaryReader reader = new(fileStream);

        folderPath = Path.Combine(outputDirectory, folderPath);
        string fullPath = Path.Combine(folderPath, fileName);

        ConsoleColor old = ConsoleColor.Gray;
        if (ShowInfo)
            old = Console.ForegroundColor;

        if (entry.ZSize == 0)
        {
            // Uncompressed file
            if (ShowInfo)
            {
                // Set console color to green
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"File: {fileName}");
            }

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(folderPath);
            using FileStream file = new($"{fullPath}", FileMode.Create, FileAccess.Write);
            // Set reader to the correct position
            fileStream.Seek(entry.Offset, SeekOrigin.Begin);
            byte[] buffer = reader.ReadBytes(entry.Size);
            file.Write(buffer, 0, buffer.Length);
        }
        else
        {
            // Compressed file
            if (ShowInfo)
            {
                // Set console color to red
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Compression: {entry.ZSize}, File: {fileName}");
            }

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(folderPath);
            using FileStream file = new($"{fullPath}", FileMode.Create, FileAccess.Write);
            // Set reader to the correct position
            fileStream.Seek(entry.Offset, SeekOrigin.Begin);
            byte[] buffer = reader.ReadBytes(entry.ZSize);
            // Decompress the buffer using zlib
            buffer = Decompressor.Decompress(buffer);
            file.Write(buffer, 0, buffer.Length);
        }

        if (ShowInfo)
            Console.ForegroundColor = old;
    }
}