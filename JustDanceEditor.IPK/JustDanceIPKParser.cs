using System.IO.Compression;

namespace JustDanceEditor.IPK;

public class JustDanceIPKParser
{
    readonly byte[] IdString = [0x50, 0xEC, 0x12, 0xBA];
    long version;
    long baseOffset;
    long filesCount;
    Stream fileStream;

    string outputDirectory;

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
        outputDirectory = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(filePath));
    }

    public void Parse()
    {
        using BinaryReader reader = new(fileStream);
        byte[] magic = reader.ReadBytes(4);
        // Check if the magic is correct
        if (!magic.SequenceEqual(IdString))
            throw new InvalidDataException("Invalid IPK file");

        version = reader.ReadInt32BigEndian();
        // Skipping dummy long
        reader.ReadInt32BigEndian();
        baseOffset = reader.ReadInt32BigEndian();
        filesCount = reader.ReadInt32BigEndian();

        fileStream.Seek(0x30, SeekOrigin.Begin);

        List<FileEntry> entries = [];

        // First read all the file entries
        for (int i = 0; i < filesCount; i++)
            entries.Add(ReadFileEntry(reader));

        // Now process the file entries
        foreach (FileEntry entry in entries)
        {
            fileStream.Seek(entry.Offset, SeekOrigin.Begin);
            ProcessFileEntry(entry, fileStream, outputDirectory);
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

	private static void ProcessFileEntry(FileEntry entry, Stream fileStream, string outputDirectory)
	{
		(string fileName, string folderPath) = entry.Name.Contains('.') ? (entry.Name, entry.Path) : (entry.Path, entry.Name);
		BinaryReader reader = new(fileStream);

        folderPath = Path.Combine(outputDirectory, folderPath);
        string fullPath = Path.Combine(folderPath, fileName);

        ConsoleColor old = Console.ForegroundColor;
		if (entry.ZSize == 0)
		{
			// Uncompressed file
			// Set console color to green
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"File: {fileName}");

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
			// Set console color to red
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Compression: {entry.ZSize}, File: {fileName}");

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(folderPath);
            using FileStream file = new($"{fullPath}", FileMode.Create, FileAccess.Write);
			// Set reader to the correct position
			fileStream.Seek(entry.Offset, SeekOrigin.Begin);
			byte[] buffer = reader.ReadBytes(entry.ZSize);
            // Decompress the buffer using zlib
            buffer = Decompress(buffer);
            file.Write(buffer, 0, buffer.Length);
        }

		Console.ForegroundColor = old;
	}

    public static byte[] Decompress(byte[] data)
    {
        using MemoryStream compressedStream = new(data);
        // Skip the first two bytes of the zlib header
        compressedStream.Seek(2, SeekOrigin.Begin);
        using DeflateStream deflateStream = new(compressedStream, CompressionMode.Decompress);
        using MemoryStream decompressedStream = new();
        deflateStream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }
}
