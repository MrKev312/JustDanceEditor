using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Images;

public sealed record UnityCoverArtRequest(UnityExportData UnityData, UnityMenuArtSource MenuArt);

public static class UnityCoverArtGenerator
{
    private static readonly HttpClient HttpClient = new();
    public static Image<Rgba32>? TryLoadCover(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryLoadImage(request.MenuArt.CoverPath);
    }

    public static Image<Rgba32>? TryLoadSongTitleLogo(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryLoadImage(request.MenuArt.SongTitleLogoPath);
    }

    public static Image<Rgba32>? TryLoadBackground(UnityCoverArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryLoadImage(request.MenuArt.CoachesBackgroundPath);
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

    private static Image<Rgba32>? TryLoadImage(string? path)
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