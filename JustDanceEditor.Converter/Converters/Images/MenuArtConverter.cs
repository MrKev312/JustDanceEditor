using Pfim;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Diagnostics;
using JustDanceEditor.Converter.Resources;

namespace JustDanceEditor.Converter.Converters.Images;

public static class MenuArtConverter
{
    public static void ConvertMenuArt(ConvertUbiArtToUnity convert)
    {
        string[] menuArtFiles = Directory.GetFiles(convert.MenuArtFolder);
        Stopwatch stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"Converting {menuArtFiles.Length} menu art files...");

        Parallel.ForEach(menuArtFiles, (item) =>
        {
            string fileName = Path.GetFileNameWithoutExtension(item);
            string tempFilePath = PrepareFileForConversion(item, convert.TempMenuArtFolder, fileName);
            ConvertFileFormat(tempFilePath, fileName, convert.TempMenuArtFolder);
        });

        stopwatch.Stop();
        Console.WriteLine($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static string PrepareFileForConversion(string originalFilePath, string tempFolder, string fileName)
    {
        string tempFilePath = Path.Combine(tempFolder, fileName + ".xtx");

        using (FileStream originalStream = File.OpenRead(originalFilePath))
        using (FileStream tempStream = File.Create(tempFilePath))
        {
            originalStream.Seek(0x2C, SeekOrigin.Begin);
            originalStream.CopyTo(tempStream);
        }

        return tempFilePath;
    }

    private static void ConvertFileFormat(string inputPath, string fileName, string tempFolder)
    {
        string ddsPath = ExtractToDDS(inputPath, tempFolder, fileName);
        ConvertDDSToPng(ddsPath, tempFolder, fileName);
    }

    private static string ExtractToDDS(string inputPath, string tempFolder, string fileName)
    {
        string outputPath = Path.Combine(tempFolder, fileName + ".dds");
        XTXExtractAdapter.ConvertToDDS(inputPath, outputPath);
        File.Delete(inputPath);
        return outputPath;
    }

    private static void ConvertDDSToPng(string ddsPath, string tempFolder, string fileName)
    {
        string pngPath = Path.Combine(tempFolder, fileName + ".png");

        using (IImage image = Pfim.Pfimage.FromFile(ddsPath))
        {
            if (image.Format != ImageFormat.Rgba32)
                throw new Exception("Image is not in Rgba32 format!");

            using var newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
            newImage.Save(pngPath);
        }

        File.Delete(ddsPath);
    }
}
