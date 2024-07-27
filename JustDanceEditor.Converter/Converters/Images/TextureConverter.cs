using JustDanceEditor.Converter.Resources;
using Pfim;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using SwitchTexture.TextureType;

namespace JustDanceEditor.Converter.Converters.Images;

internal class TextureConverter
{
    public static Image<Bgra32> ConvertToImage(string inputPath)
    {
        // If the file starts with "DDS ", it's already a DDS file and we simply rename it
        if (File.ReadAllBytes(inputPath).Take(4).SequenceEqual("DDS "u8.ToArray()))
        {
            using IImage image = Pfimage.FromFile(inputPath);
            if (image.Format != ImageFormat.Rgba32)
                throw new Exception("Image is not in Rgba32 format!");

            Image<Bgra32> newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
            return newImage;
        }
        else if (File.ReadAllBytes(inputPath).Take(4).SequenceEqual("DFvN"u8.ToArray()))
        {
            // Convert the XTX file to DDS
            Image<Bgra32> newImage = XTX.ConvertToImage(inputPath);

            File.Delete(inputPath);

            return newImage;
        }

        throw new Exception("Unknown file format!");
    }

    public static string ExtractToPNG(string inputPath, string tempFolder, string fileName)
    {
        string outputPath = Path.Combine(tempFolder, fileName + ".png");

        using Image<Bgra32> image = ConvertToImage(inputPath);
        image.Save(outputPath);

        return outputPath;
    }
}
