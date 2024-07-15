using JustDanceEditor.Converter.Resources;

using Pfim;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters.Images;

public static class PictoConverter
{
    public static (Dictionary<string, (int, (int, int))> ImageDictionary, List<Image<Rgba32>> AtlasPics) ConvertPictos(ConvertUbiArtToUnity convert)
    {
        // Before starting on the mapPackage, prepare the pictos
        string[] pictoFiles = Directory.GetFiles(convert.PictosFolder);
        Console.WriteLine($"Converting {pictoFiles.Length} pictos...");

        // Get time before starting
        Stopwatch stopwatch = Stopwatch.StartNew();

        Parallel.For(0, pictoFiles.Length, i =>
        {
            string item = pictoFiles[i];

            // Stream the file into a new pictos folder, but skip until 0x2C
            using FileStream stream = File.OpenRead(item);
            stream.Seek(0x2C, SeekOrigin.Begin);

            // If the file ends with .png.ckd, change it to .ckd
            if (item.EndsWith(".png.ckd"))
                item = item.Replace(".png.ckd", ".ckd");

            // Get the file name
            string fileName = Path.GetFileNameWithoutExtension(item);

            // Stream the rest of the file into a new file, but with the .xtx extension
            using FileStream newStream = File.Create(Path.Combine(convert.TempPictoFolder, fileName + ".xtx"));
            stream.CopyTo(newStream);

            // Close the streams
            stream.Close();
            newStream.Close();

            // If the file starts with "DDS ", it's already a DDS file and we simply rename it
            if (File.ReadAllBytes(Path.Combine(convert.TempPictoFolder, fileName + ".xtx")).Take(4).SequenceEqual("DDS "u8.ToArray()))
            {
                File.Move(Path.Combine(convert.TempPictoFolder, fileName + ".xtx"), Path.Combine(convert.TempPictoFolder, fileName + ".dds"));
            }
            else
            {
                // Run xtx_extract on the new file, parameters: -o {filename}.dds {filename}.xtx
                // Print the output to the console
                XTXExtractAdapter.ConvertToDDS(Path.Combine(convert.TempPictoFolder, fileName + ".xtx"), Path.Combine(convert.TempPictoFolder, fileName + ".dds"));

                // Delete the .xtx file
                File.Delete(Path.Combine(convert.TempPictoFolder, fileName + ".xtx"));
            }

            // Convert the .dds file to .png
            Image<Bgra32> newImage;
            using (IImage image = Pfimage.FromFile(Path.Combine(convert.TempPictoFolder, fileName + ".dds")))
            {

                // If the image is not in Rgba32 format, throw an exception
                if (image.Format != ImageFormat.Rgba32)
                    throw new Exception("Image is not in Rgba32 format!");

                // Create image from image.Data
                newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
            }

            if (convert.SongData.CoachCount > 1)
            {
                // For multi-coach songs, resize the image to 512x354
                if (newImage.Width != 512 || newImage.Height != 354)
                    newImage.Mutate(x => x.Resize(512, 354));
            }
            else
            {
                // If the image isn't 512x512, resize it to 512x512
                if (newImage.Width != 512 || newImage.Height != 512)
                    newImage.Mutate(x => x.Resize(512, 512));
            }

            // Delete the .dds file
            File.Delete(Path.Combine(convert.TempPictoFolder, fileName + ".dds"));

            // Save the image as a png
            newImage.Save(Path.Combine(convert.TempPictoFolder, fileName + ".png"));

            // Dispose the image
            newImage.Dispose();
        });

        Dictionary<string, (int, (int, int))> imageDict = [];
        List<Image<Rgba32>> atlasPics = [];
        Image<Rgba32>? atlasImage = null;

        Console.WriteLine("Creating atlasses...");

        // Get the files in the pictos folder
        pictoFiles = Directory.GetFiles(convert.TempPictoFolder);

        // Convert the 512x512 images to a 2048x2048 atlas
        // Use 4 pixels of padding between each image
        for (int i = 0; i < pictoFiles.Length; i++)
        {
            int indexInAtlas = i % 16;

            if (indexInAtlas == 0 || atlasImage is null)
                // Create a new image
                atlasImage = new(2048, 2048);

            // Get the current image
            (Image<Rgba32> image, string name) = (Image.Load<Rgba32>(pictoFiles[i]), Path.GetFileNameWithoutExtension(pictoFiles[i]));


            // Get the x and y coordinates
            int x_coord = indexInAtlas % 4 * 512;
            int y_coord = indexInAtlas / 4 * 512;

            // Because the y is calculated from the top left corner, we need to subtract it from 2048
            y_coord = 2048 - y_coord - 512;

            // Shift the y coordinate by 512 - image.Height to adjust for the image being at the bottom
            y_coord += 512 - image.Height;

            // Draw the image on the atlas
            atlasImage.Mutate(x => x.DrawImage(image, new Point(x_coord, y_coord), 1));

            // Dispose the image
            image.Dispose();

            // Store the name and the atlas index in the dictionary
            imageDict.Add(name, (i / 16, (image.Width, image.Height)));

            // If this is the last image, or i % 16 == 15, add the image to the atlasPics array
            if (indexInAtlas == 15 || i == pictoFiles.Length - 1)
            {
                // Add the image to the atlasPics array
                atlasPics.Add(atlasImage);

                // Set to null but don't dispose
                atlasImage = null;
            }
        }

        // Save the atlasPics in the tempPictoFolder in the format atlas_{index}.png
        Directory.CreateDirectory(Path.Combine(convert.TempPictoFolder, "Atlas"));

        for (int i = 0; i < atlasPics.Count; i++)
            atlasPics[i].Save(Path.Combine(convert.TempPictoFolder, "Atlas", $"atlas_{i}.png"));

        // Get time after finishing
        stopwatch.Stop();
        Console.WriteLine($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms");

        return (imageDict, atlasPics);
    }
}
