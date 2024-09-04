using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.TextureType;

namespace TextureConverter;

public class TextureConverter
{
    public static Image<Bgra32> ConvertToImage(string inputPath, bool deleteOriginal = false)
    {
        // Check if input exists
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input file not found!", inputPath);

        // Open stream and read header
        FileStream fileStream = File.OpenRead(inputPath);
        BinaryReader reader = new(fileStream);

        // If it starts with "\0\0\0\tTEX", skip to 0xD8
        byte[] texHeader = [0x00, 0x00, 0x00, 0x09, 0x54, 0x45, 0x58];

        if (reader.ReadBytes(7).SequenceEqual(texHeader))
            fileStream.Seek(0x2C, SeekOrigin.Begin);
        else
            fileStream.Seek(0, SeekOrigin.Begin);

        // Now peek 4 bytes to determine the format
        string header = Encoding.ASCII.GetString(reader.ReadBytes(4));
        reader.BaseStream.Seek(-4, SeekOrigin.Current);

        Image<Bgra32> image = header switch
        {
            "DDS " => DDS.GetImage(fileStream),
            "DFvN" => XTX.GetImage(fileStream),
            "Gfx2" => GTX.GetImage(fileStream),
            _ => Image.Load<Bgra32>(fileStream)
        };

        // Close stream and delete original file if needed
        fileStream.Dispose();
        if (deleteOriginal)
            File.Delete(inputPath);

        return image;
    }

    public static string ExtractToPNG(string inputPath, string? outputPath, bool deleteOriginal = false)
    {
        outputPath ??= Path.ChangeExtension(inputPath, ".png");

        using Image<Bgra32> image = ConvertToImage(inputPath, deleteOriginal);
        image.Save(outputPath);

        return outputPath;
    }
}
