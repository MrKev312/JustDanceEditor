using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using SwitchTexture.TextureType;

using System.Text;

namespace SwitchTexture;

public class TextureConverter
{
    public static Image<Bgra32> ConvertToImage(string inputPath, bool deleteOriginal = true)
    {
        string header = Encoding.ASCII.GetString(File.ReadAllBytes(inputPath).Take(4).ToArray());

        Image<Bgra32> image = header switch
        {
            "DDS " => DDS.GetImage(inputPath),
            "DFvN" => XTX.GetImage(inputPath),
            _ => throw new Exception("Unknown file format!"),
        };

        if (deleteOriginal)
            File.Delete(inputPath);

        return image;
    }

    public static string ExtractToPNG(string inputPath, string? outputPath, bool deleteOriginal = true)
    {
        outputPath ??= Path.ChangeExtension(inputPath, ".png");

        using Image<Bgra32> image = ConvertToImage(inputPath, deleteOriginal);
        image.Save(outputPath);

        return outputPath;
    }
}
