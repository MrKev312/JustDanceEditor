using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System.Text.Json;

namespace JustDanceEditor.Formats.JDI.Services;

/// <summary>
/// Service for downloading online assets for JDI packages from the internet.
/// </summary>
public class OnlineAssetDownloader(ILogger? logger = null)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/MrKev312/JustDanceCovers/refs/heads/main/";
    private const string CoversJsonUrl = $"{BaseUrl}/Covers.json";
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Downloads available online assets for a song and saves them to the intermediate package.
    /// </summary>
    /// <param name="packageRoot">The root path of the intermediate package.</param>
    /// <param name="package">The intermediate song package containing metadata about coaches needed.</param>
    /// <param name="io">The file system implementation to use.</param>
    public async Task DownloadAssetsAsync(string packageRoot, IntermediateSongPackage package, IFileSystem? io = null)
    {
        IFileSystem iofs = io ?? new SystemFileSystem();

        string songName = package.Metadata.MapName;
        logger?.LogInformation("Downloading online assets for '{SongName}'...", songName);

        // Collect all download tasks to run concurrently
        List<Task> downloadTasks = [];

        // Base assets (always try to download)
        downloadTasks.Add(TryDownloadAssetAsync(songName, "Cover", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.CoverFile), iofs));
        downloadTasks.Add(TryDownloadAssetAsync(songName, "Title", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.SongTitleFile), iofs));
        downloadTasks.Add(TryDownloadAssetAsync(songName, "Square", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile), iofs));
        downloadTasks.Add(TryDownloadAssetAsync(songName, "Background", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.MapBackgroundFile), iofs));
        downloadTasks.Add(TryDownloadAssetAsync(songName, "AlbumCoach", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.AlbumCoachFile), iofs));
        downloadTasks.Add(TryDownloadAssetAsync(songName, "Banner", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.BannerFile), iofs));

        // Download coaches based on the number needed (1-6)
        int coachCount = Math.Min(package.Metadata.CoachCount, 6);
        for (int i = 1; i <= coachCount; i++)
        {
            int coachNumber = i;
            downloadTasks.Add(TryDownloadAssetAsync(songName, $"Coach_{coachNumber}", Path.Combine(packageRoot, IntermediatePackageLayout.Assets.CoachFile(coachNumber)), iofs));
        }

        // Execute all downloads concurrently
        await Task.WhenAll(downloadTasks);

        logger?.LogInformation("Online asset download completed for '{SongName}' ({CoachCount} coaches)", songName, coachCount);
    }

    private async Task TryDownloadAssetAsync(string songName, string assetType, string destination, IFileSystem io)
    {
        try
        {
            // Try direct download first
            string url = $"{BaseUrl}/Covers/{songName}/{assetType}.webp";
            using (HttpResponseMessage response = await _httpClient.GetAsync(url))
            {
                if (response.IsSuccessStatusCode)
                {
                    await SaveAssetFromResponse(destination, response, io);
                    logger?.LogInformation("Downloaded '{AssetType}' for '{SongName}'", assetType, songName);
                    return;
                }
            }

            // Try codename lookup if direct download failed
            string? codename = await ResolveCodename(songName);
            if (codename is not null && codename != songName)
            {
                url = $"{BaseUrl}{codename}/{assetType}.webp";
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    await SaveAssetFromResponse(destination, response, io);
                    logger?.LogInformation("Downloaded '{AssetType}' for '{SongName}' (via codename '{Codename}')", assetType, songName, codename);
                    return;
                }
            }

            logger?.LogDebug("Online asset '{AssetType}' not available for '{SongName}'", assetType, songName);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Failed to download '{AssetType}' for '{SongName}': {Message}", assetType, songName, ex.Message);
        }
    }

    private async Task SaveAssetFromResponse(string fullPath, HttpResponseMessage response, IFileSystem io)
    {
        io.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
        using Stream contentStream = await response.Content.ReadAsStreamAsync();
        using FileStream fileStream = File.Open(fullPath, FileMode.Create, FileAccess.Write);
        await contentStream.CopyToAsync(fileStream);
    }

    private async Task<string?> ResolveCodename(string mapName)
    {
        try
        {
            string json = await _httpClient.GetStringAsync(CoversJsonUrl);
            Dictionary<string, string[]>? covers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);

            if (covers is null)
                return null;

            string? codename = covers
                .Where(x => x.Value?.Contains(mapName) ?? false)
                .Select(x => x.Key)
                .FirstOrDefault();

            return codename;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Failed to resolve codename for '{MapName}': {Message}", mapName, ex.Message);
            return null;
        }
    }
}