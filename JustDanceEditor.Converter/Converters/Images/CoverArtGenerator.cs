using HtmlAgilityPack;

using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace JustDanceEditor.Converter.Converters.Images;

public static class CoverArtGenerator
{
    public static Image<Rgba32>? ExistingCover(ConvertUbiArtToUnity convert)
    {
        string[] paths =
        [
            Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, "cover.png"),
            Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_cover_online.png"),
            Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_cover_generic.png")
        ];

        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;

            Image<Rgba32>? image = TryLoadImage(path);
            if (image is null)
                continue;

            // Check if the image is square-ish, must be wider than 4:3
            if (image.Width < image.Height * 1.33)
            {
                image.Dispose();
                continue;
            }

            string fileName = Path.GetFileName(path);

            // Log that we found an existing usable cover
            Logger.Log($"Found existing cover: {fileName}", LogLevel.Important);

            // Resize the image to 640x360
            image.Mutate(x => x.Resize(640, 360));

            return image;
        }

        return null;
    }

    static Image<Rgba32>? TryLoadImage(string path)
    {
        if (!File.Exists(path))
            return null;

        // Load the image
        Image<Rgba32> coverImage = Image.Load<Rgba32>(path);

        return coverImage;
    }

    public static Image<Rgba32>? TryCoverWeb(ConvertUbiArtToUnity convert)
    {
        // Download one from https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B
        // Load the webpage
        HttpClient client = new();
        string html = client.GetStringAsync("https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B").Result;

        // Convert the html to a document
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        // Get the class called "fandom-table"[0]/tbody
        HtmlNode table = doc.DocumentNode.SelectNodes("//table[@class='fandom-table']")[0];
        HtmlNode htmlNode = table.SelectSingleNode("tbody");

        // Get all the tr nodes
        List<HtmlNode> allNodes = htmlNode.SelectNodes("tr").Skip(1).ToList();

        // Find the node where the first td's inner text is the map name
        HtmlNode? row = null;

        foreach (HtmlNode node in allNodes)
        {
            // Get the first <i> tag's title, it might not be a direct child
            string title = node.SelectSingleNode(".//i").InnerText;

            if (title == convert.SongData.SongDesc.COMPONENTS[0].Title)
            {
                row = node;
                break;
            }
        }

        // If the cover doesn't exist, return false
        if (row is null)
            return null;

        // If both the last or second to last td's are empty or "N/A", then the cover doesn't exist
        HtmlNodeCollection tds = row.SelectNodes("td");

        // Get the cover url with text on it
        string coverUrl = tds[^1].SelectSingleNode("a").Attributes["href"].Value;
        // If this is a placeholder cover, get the second to last, the version without text
        if (coverUrl.Contains("PlaceHolderCover", StringComparison.OrdinalIgnoreCase))
            coverUrl = tds[^2].SelectSingleNode("a").Attributes["href"].Value;
        if (coverUrl.Contains("PlaceHolderCover", StringComparison.OrdinalIgnoreCase))
            // Couldn't find the cover, return false
            return null;

        // Load the image
        Stream coverStream = client.GetStreamAsync(coverUrl).Result;
        Image<Rgba32>? coverImage = Image.Load<Rgba32>(coverStream);
        coverImage.Mutate(x => x.Resize(640, 360));

        // Success!
        return coverImage;
    }

    public static Image<Rgba32> GenerateOwnCover(ConvertUbiArtToUnity convert)
    {
        // Manually create the cover
        // Now we gotta make a custom texture from the scraps we have in the menu art folder
        Image<Rgba32>? coverImage = GetBackground(convert);

        // Then we load in the albumcoach
        string albumCoachPath = Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_cover_albumcoach.png");
        Image<Rgba32>? albumCoach = TryLoadImage(albumCoachPath);
        albumCoach ??= TryLoadImage(Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_Coach_1.png"));
        albumCoach ??= new Image<Rgba32>(1024, 1024);

        albumCoach.Mutate(x => x.Resize(1024, 1024));


        // Then we place the albumcoach on top of the background in the center
        // The background is 2048x1024 and the albumcoach is 1024x1024
        // So we place it at 512, 0
        coverImage.Mutate(x => x.DrawImage(albumCoach, new Point(512, 0), 1));

        // Now we resize the image down to 720x360
        coverImage.Mutate(x => x.Resize(720, 360));

        // Now we crop the image to 640x360 centered
        coverImage.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));

        return coverImage;
    }

    public static Image<Rgba32> GetBackground(ConvertUbiArtToUnity convert)
    {
        // Try either the map or banner background
        Image<Rgba32>? coverImage = TryLoadImage(Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_map_bkg.png"));
        coverImage ??= ProcessBanner(convert);

        if (coverImage is null)
        {
            // If the cover doesn't exist, create a new one
            coverImage = new Image<Rgba32>(2048, 1024);
            coverImage.Mutate(x => x.BackgroundColor(Color.Magenta));
            // Write the text "Cover not found" in the center
            Font font = SystemFonts.CreateFont("Segoe UI", 150, FontStyle.Regular);
            coverImage.Mutate(x => x.DrawText("Background not found", font, Color.FloralWhite, new PointF(200, 512)));
        }

        coverImage.Mutate(x => x.Resize(2048, 1024));

        return coverImage;
    }

    public static Image<Rgba32>? ProcessBanner(ConvertUbiArtToUnity convert)
    {
        string path = Path.Combine(convert.FileSystem.TempFolders.MenuArtFolder, $"{convert.SongData.Name}_banner_bkg.png");
        using Image<Rgba32>? banner = TryLoadImage(path);
        if (banner is null)
            return null;

        int width = banner.Width;
        int height = banner.Height;

        // If the engine is 2019 or newer, use 2a
        // Arrays of Argb values
        float[] colorsA;
        float[] colorsB;
        if (convert.SongData.EngineVersion >= UbiArt.JDVersion.JD2019)
        {
            colorsA = convert.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_1a;
            colorsB = convert.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_1b;
        }
        else
        {
            colorsA = convert.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_2a;
            colorsB = convert.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_2b;
        }

        // Create a new image with the same size
        Rgba32 colorA = new((byte)(colorsA[1] * 255), (byte)(colorsA[2] * 255), (byte)(colorsA[3] * 255), (byte)(colorsA[0] * 255));
        Rgba32 colorB = new((byte)(colorsB[1] * 255), (byte)(colorsB[2] * 255), (byte)(colorsB[3] * 255), (byte)(colorsB[0] * 255));

        // Create a new image with the same size, filled with the first color
        Image<Rgba32> resultImage = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Get the color of the pixel
                Rgba32 pixel = banner[x, y];

                // Get weighted average of the two colors based on the blue channel
                float weight = pixel.B / 255f;
                Rgba32 newColor = GetWeightedAverage(colorA, colorB, weight);

                // Add the green channel of the pixel to the new color
                newColor = AddGreenChannel(newColor, pixel.G);

                // Set the new color
                resultImage[x, y] = newColor;
            }
        }

        return resultImage;

        static Rgba32 GetWeightedAverage(Rgba32 colorA, Rgba32 colorB, float weight)
        {
            return new(
                (byte)((colorA.R * weight) + (colorB.R * (1 - weight))),
                (byte)((colorA.G * weight) + (colorB.G * (1 - weight))),
                (byte)((colorA.B * weight) + (colorB.B * (1 - weight))),
                (byte)((colorA.A * weight) + (colorB.A * (1 - weight)))
            );
        }

        static Rgba32 AddGreenChannel(Rgba32 color, byte greenValue)
        {
            return new(
                (byte)Math.Min(color.R + greenValue, byte.MaxValue),
                (byte)Math.Min(color.G + greenValue, byte.MaxValue),
                (byte)Math.Min(color.B + greenValue, byte.MaxValue),
                color.A // Keep the alpha channel unchanged
            );
        }
    }
}
