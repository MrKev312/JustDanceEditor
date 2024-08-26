using JustDanceEditor.Converter.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters.Images;

public static class PictoConverter
{
    public static (Dictionary<string, (int, (int, int))> ImageDictionary, List<Image<Rgba32>> AtlasPics) ConvertPictos(ConvertUbiArtToUnity convert)
    {
        PictogramClip[] pictoClips = convert.SongData.Clips.OfType<PictogramClip>().ToArray();

        // Before starting on the mapPackage, prepare the pictos
        if (!Directory.Exists(convert.PictosFolder))
        {
            Logger.Log("Pictos folder doesn't exist, skipping picto conversion", LogLevel.Warning);
            return ([], []);
        }

        string[] pictoFiles = Directory.GetFiles(convert.PictosFolder);

        if (pictoFiles.Length == 0)
        {
            Logger.Log("No pictos found, skipping picto conversion", LogLevel.Warning);
            return ([], []);
        }

        Logger.Log($"Converting {pictoFiles.Length} pictos...");

        // Get time before starting
        Stopwatch stopwatch = Stopwatch.StartNew();

        bool isMontage = false;

        Parallel.For(0, pictoFiles.Length, i =>
        {
            string item = pictoFiles[i];

            // Get the name until the first dot, can have multiple dots
            string name = Path.GetFileName(item).Split('.')[0];

            if (name == "montage")
            {
                isMontage = true;
            }

            // Stream the file into a new pictos folder
            Image<Bgra32> pictoPic = TextureConverter.TextureConverter.ConvertToImage(item);

            if (isMontage)
            {
                SplitMontage(pictoPic, convert);
                return;
            }

            if (convert.SongData.CoachCount > 1)
            {
                // For multi-coach songs, resize the image to 512x354
                if (pictoPic.Width != 512 || pictoPic.Height != 354)
                    pictoPic.Mutate(x => x.Resize(512, 354));
            }
            else
            {
                // If the image isn't 512x512, resize it to 512x512
                if (pictoPic.Width != 512 || pictoPic.Height != 512)
                    pictoPic.Mutate(x => x.Resize(512, 512));
            }

            // Save the image as a png
            pictoPic.Save(Path.Combine(convert.TempPictoFolder, name + ".png"));

            // Dispose the image
            pictoPic.Dispose();
        });

        Dictionary<string, (int, (int, int))> imageDict = [];
        List<Image<Rgba32>> atlasPics = [];
        Image<Rgba32>? atlasImage = null;

        Logger.Log("Creating atlasses...");

        // Get the png files in the pictos folder
        pictoFiles = Directory.GetFiles(convert.TempPictoFolder, "*.png");

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
        Directory.CreateDirectory(convert.TempPictoAtlasFolder);

        for (int i = 0; i < atlasPics.Count; i++)
            atlasPics[i].Save(Path.Combine(convert.TempPictoFolder, "Atlas", $"atlas_{i}.png"));

        // Get time after finishing
        stopwatch.Stop();
        Logger.Log($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms");

        return (imageDict, atlasPics);
    }

    static void SplitMontage(Image<Bgra32> montage, ConvertUbiArtToUnity convert)
    {
        List<string> pictoNames = [];

        foreach (PictogramClip clip in convert.SongData.Clips.OfType<PictogramClip>())
        {
            string name = Path.GetFileNameWithoutExtension(clip.PictoPath);

            if (!pictoNames.Contains(name))
                pictoNames.Add(name);
        }

        // Sort alphabetically
        pictoNames.Sort();
        int pictoCount = pictoNames.Count;
        int columns = 8;
        int rows = 1;
        while (columns * rows < pictoCount)
        {
            rows++;
        }

        // Get the width and height of the montage
        int width = montage.Width;
        int height = montage.Height;

        // Given a picto is 512x512 with 64px vertical padding, we split the montage into columns * rows pictos
        int pictoHeight = width / columns;
        int pictoWidth = height / rows;

        // Split the montage into pictos
        for (int i = 0; i < pictoCount; i++)
        {
            // Calculate row and column
            int row = i / columns;
            int col = i % columns;

            // Extract the portion of the montage
            Image<Bgra32> picto = montage.Clone(x => x.Crop(new Rectangle(col * pictoHeight, row * pictoWidth, pictoHeight, pictoWidth)));

            // Resize to 512x512
            picto.Mutate(x => x.Resize(512, 512));

            // Save or use the extracted picto (e.g., saving to disk)
            string pictoName = pictoNames[i];
            string pictoPath = Path.Combine(convert.TempPictoFolder, $"{pictoName}.png");
            picto.SaveAsPng(pictoPath);
        }
    }
}
