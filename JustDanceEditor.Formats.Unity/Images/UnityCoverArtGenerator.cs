using JustDanceEditor.Logging;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Images;

public sealed record UnityCoverArtRequest(UnityExportData UnityData, string MenuArtFolder);

public static class UnityCoverArtGenerator
{
    private static readonly HttpClient HttpClient = new();

    public static Image<Rgba32>? ExistingCover(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        UnityExportData song = request.UnityData ?? throw new ArgumentNullException(nameof(request.UnityData));
        if (string.IsNullOrWhiteSpace(request.MenuArtFolder))
            throw new ArgumentException("Menu art folder path is required.", nameof(request.MenuArtFolder));

        string folder = request.MenuArtFolder;
        string[] paths =
        [
            Path.Combine(folder, "cover.png"),
            Path.Combine(folder, $"{song.Name}_cover_online.png"),
            Path.Combine(folder, $"{song.Name}_cover_generic.png")
        ];

        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;

            Image<Rgba32>? image = TryLoadImage(path);
            if (image is null)
                continue;

            if (image.Width < image.Height * 1.33)
            {
                image.Dispose();
                continue;
            }

            Logger.Log($"Found existing cover: {Path.GetFileName(path)}", LogLevel.Important);
            image.Mutate(x => x.Resize(640, 360));
            return image;
        }

        return null;
    }

    public static Image<Rgba32>? ExistingSongTitleLogo(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MenuArtFolder))
            throw new ArgumentException("Menu art folder path is required.", nameof(request.MenuArtFolder));

        return TryLoadImage(Path.Combine(request.MenuArtFolder, "songTitleLogo.png"));
    }

    public static Image<Rgba32>? TryImageWeb(string mapName, string imageType)
    {
        string baseUrl = "https://raw.githubusercontent.com/MrKev312/JustDanceCovers/refs/heads/main/";

        Image<Rgba32>? FetchCoverFromWeb(string name)
            => LoadFromUrl($"{baseUrl}/Covers/{name}/{imageType}.webp");

        Image<Rgba32>? coverImage = FetchCoverFromWeb(mapName);
        if (coverImage is not null)
        {
            Logger.Log($"Found {imageType} on the web", LogLevel.Important);
            return coverImage;
        }

        string coversJsonUrl = $"{baseUrl}/Covers.json";
        string json = HttpClient.GetStringAsync(coversJsonUrl).Result;
        Dictionary<string, string[]> covers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)!;

        string? codename = covers.Where(x => x.Value.Contains(mapName)).Select(x => x.Key).FirstOrDefault();
        if (codename is null)
            return null;

        return FetchCoverFromWeb(codename);
    }

    public static Image<Rgba32> GenerateOwnCover(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        UnityExportData song = request.UnityData ?? throw new ArgumentNullException(nameof(request.UnityData));

        Image<Rgba32> coverImage = GetBackground(request);

        string folder = request.MenuArtFolder;
        string albumCoachPath = Path.Combine(folder, $"{song.Name}_cover_albumcoach.png");
        Image<Rgba32>? albumCoach = TryLoadImage(albumCoachPath)
            ?? TryLoadImage(Path.Combine(folder, $"{song.Name}_Coach_1.png"))
            ?? new Image<Rgba32>(1024, 1024);

        albumCoach.Mutate(x => x.Resize(1024, 1024));
        coverImage.Mutate(x => x.DrawImage(albumCoach, new Point(512, 0), 1));
        albumCoach.Dispose();

        coverImage.Mutate(x => x.Resize(720, 360));
        coverImage.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));
        return coverImage;
    }

    public static Image<Rgba32> GetBackground(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        UnityExportData song = request.UnityData ?? throw new ArgumentNullException(nameof(request.UnityData));
        if (string.IsNullOrWhiteSpace(request.MenuArtFolder))
            throw new ArgumentException("Menu art folder path is required.", nameof(request.MenuArtFolder));

        string folder = request.MenuArtFolder;
        Image<Rgba32>? coverImage = TryLoadImage(Path.Combine(folder, $"{song.Name}_map_bkg.png"));
        coverImage ??= ProcessBanner(request);

        if (coverImage is null)
        {
            coverImage = new Image<Rgba32>(2048, 1024);
            coverImage.Mutate(x => x.BackgroundColor(Color.Magenta));
            Font font = SystemFonts.CreateFont("Segoe UI", 150, FontStyle.Regular);
            coverImage.Mutate(x => x.DrawText("Background not found", font, Color.FloralWhite, new PointF(200, 512)));
        }

        coverImage.Mutate(x => x.Resize(2048, 1024));
        return coverImage;
    }

    public static Image<Rgba32>? ProcessBanner(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        UnityExportData song = request.UnityData ?? throw new ArgumentNullException(nameof(request.UnityData));
        if (string.IsNullOrWhiteSpace(request.MenuArtFolder))
            throw new ArgumentException("Menu art folder path is required.", nameof(request.MenuArtFolder));

        string path = Path.Combine(request.MenuArtFolder, $"{song.Name}_banner_bkg.png");
        using Image<Rgba32>? banner = TryLoadImage(path);
        if (banner is null)
            return null;

        int width = banner.Width;
        int height = banner.Height;

        UnityExportMetadata meta = song.Metadata;
        Rgba32 primaryColor = ParseColor(meta.LyricsColor, new Rgba32(255, 255, 255, 255));
        float boost = meta.EngineVersion >= 2019 ? 0.2f : -0.1f;
        Rgba32 secondaryColor = AdjustBrightness(primaryColor, boost);
        float[] colorsA = [primaryColor.A / 255f, primaryColor.R / 255f, primaryColor.G / 255f, primaryColor.B / 255f];
        float[] colorsB = [secondaryColor.A / 255f, secondaryColor.R / 255f, secondaryColor.G / 255f, secondaryColor.B / 255f];

        Rgba32 colorA = new((byte)(colorsA[1] * 255), (byte)(colorsA[2] * 255), (byte)(colorsA[3] * 255), (byte)(colorsA[0] * 255));
        Rgba32 colorB = new((byte)(colorsB[1] * 255), (byte)(colorsB[2] * 255), (byte)(colorsB[3] * 255), (byte)(colorsB[0] * 255));

        Image<Rgba32> resultImage = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgba32 pixel = banner[x, y];
                float weight = pixel.B / 255f;
                Rgba32 newColor = GetWeightedAverage(colorA, colorB, weight);
                newColor = AddGreenChannel(newColor, pixel.G);
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
                color.A);
        }
    }

    private static Image<Rgba32>? TryLoadImage(string path)
    {
        if (!File.Exists(path))
            return null;

        return Image.Load<Rgba32>(path);
    }

    private static Image<Rgba32>? LoadFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        using HttpResponseMessage response = HttpClient.GetAsync(url).Result;
        if (!response.IsSuccessStatusCode)
            return null;

        byte[] imageData = response.Content.ReadAsByteArrayAsync().Result;
        using MemoryStream stream = new(imageData);
        return Image.Load<Rgba32>(stream);
    }

    private static Rgba32 ParseColor(string? hex, Rgba32 fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string value = hex.TrimStart('#');
        if (value.Length is not (6 or 8))
            return fallback;

        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        byte a = value.Length == 8 ? Convert.ToByte(value.Substring(6, 2), 16) : (byte)255;
        return new Rgba32(r, g, b, a);
    }

    private static Rgba32 AdjustBrightness(Rgba32 color, float delta)
    {
        static byte Clamp(float value) => (byte)Math.Clamp(value, 0, 255);
        float factor = 1 + delta;
        return new Rgba32(
            Clamp(color.R * factor),
            Clamp(color.G * factor),
            Clamp(color.B * factor),
            color.A);
    }
}