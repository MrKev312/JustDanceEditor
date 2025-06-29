using JustDanceEditor.Converter.Core;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

using System.Text.Json;

namespace JustDanceEditor.Converter.Converters.Images;

public static class CoverArtGenerator
{
    private static readonly HttpClient httpClient = new();

    public static Image<Rgba32>? ExistingCover(ConversionContext context)
    {
        string[] paths =
        [
            Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, "cover.png"),
            Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_cover_online.png"),
            Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_cover_generic.png")
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

    public static Image<Rgba32>? ExistingSongTitleLogo(ConversionContext context)
    {
        // Load the image
        return TryLoadImage(Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, "songTitleLogo.png"));
    }

    static Image<Rgba32>? TryLoadImage(string path)
    {
        if (!File.Exists(path))
            return null;

        // Load the image
        Image<Rgba32> coverImage = Image.Load<Rgba32>(path);

        return coverImage;
    }

    public static Image<Rgba32>? TryImageWeb(ConversionContext context, string imageType)
    {
        string baseUrl = "https://raw.githubusercontent.com/MrKev312/JustDanceCovers/refs/heads/main/";

        Image<Rgba32>? FetchCoverFromWeb(string name)
            => LoadFromUrl($"{baseUrl}/Covers/{name}/{imageType}.webp");

        Image<Rgba32>? coverImage = FetchCoverFromWeb(context.SongData.Name);

        if (coverImage is not null)
        {
            Logger.Log($"Found {imageType} on the web", LogLevel.Important);
            return coverImage;
        }

        // Else, maybe we can look in the Covers.json
        string coversJsonUrl = $"{baseUrl}/Covers.json";
        string json = httpClient.GetStringAsync(coversJsonUrl).Result;
        Dictionary<string, string[]> covers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)!;

        string? codename = covers.Where(x => x.Value.Contains(context.SongData.Name)).Select(x => x.Key).FirstOrDefault();
       if (codename is null)
            return null;

        // Now we can try to fetch the cover from the web
        return FetchCoverFromWeb(codename);
    }

    private static Image<Rgba32>? LoadFromUrl(string url)
    {
        // Return null if the URL is invalid or the request fails
        if (string.IsNullOrEmpty(url))
            return null;

        // Send a GET request to the URL
        using HttpResponseMessage response = httpClient.GetAsync(url).Result;

        // Check if the request was successful
        if (response.IsSuccessStatusCode)
        {
            // Read the image data from the response
            byte[] imageData = response.Content.ReadAsByteArrayAsync().Result;
            // Load the image from the byte array
            using MemoryStream stream = new(imageData);
            return Image.Load<Rgba32>(stream);
        }

        return null;
    }

    public static Image<Rgba32> GenerateOwnCover(ConversionContext context)
    {
        // Manually create the cover
        // Now we gotta make a custom texture from the scraps we have in the menu art folder
        Image<Rgba32>? coverImage = GetBackground(context);

        // Then we load in the albumcoach
        string albumCoachPath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_cover_albumcoach.png");
        Image<Rgba32>? albumCoach = TryLoadImage(albumCoachPath);
        albumCoach ??= TryLoadImage(Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_Coach_1.png"));
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

    public static Image<Rgba32> GetBackground(ConversionContext context)
    {
        // Try either the map or banner background
        Image<Rgba32>? coverImage = TryLoadImage(Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_map_bkg.png"));
        coverImage ??= ProcessBanner(context);

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

    public static Image<Rgba32>? ProcessBanner(ConversionContext context)
    {
        string path = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_banner_bkg.png");
        using Image<Rgba32>? banner = TryLoadImage(path);
        if (banner is null)
            return null;

        int width = banner.Width;
        int height = banner.Height;

        // If the engine is 2019 or newer, use 2a
        // Arrays of Argb values
        float[] colorsA;
        float[] colorsB;
        if (context.SongData.EngineVersion >= 2019)
        {
            colorsA = context.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_1a;
            colorsB = context.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_1b;
        }
        else
        {
            colorsA = context.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_2a;
            colorsB = context.SongData.SongDesc.COMPONENTS[0].DefaultColors.songcolor_2b;
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
