using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Images;

public static class ImageLoader
{
    private static readonly HttpClient HttpClient = new();

    public static Image<Rgba32>? TryImageWeb(string mapName, string imageType, Microsoft.Extensions.Logging.ILogger logger)
    {
        string baseUrl = "https://raw.githubusercontent.com/MrKev312/JustDanceCovers/refs/heads/main/";

        Image<Rgba32>? FetchCoverFromWeb(string name)
            => LoadFromUrl($"{baseUrl}/Covers/{name}/{imageType}.webp");

        Image<Rgba32>? coverImage = FetchCoverFromWeb(mapName);
        if (coverImage is not null)
        {
            logger.LogInformation("Found {ImageType} on the web", imageType);
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

    public static Image<Rgba32>? TryLoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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
}