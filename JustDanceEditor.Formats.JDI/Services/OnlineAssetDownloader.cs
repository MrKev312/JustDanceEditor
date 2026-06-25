using Microsoft.Extensions.Logging;

using System.Net;
using System.Text.Json;

namespace JustDanceEditor.Formats.JDI.Services;

public static class OnlineAssetKeys
{
    public const string Cover = "cover";
    public const string SquareCover = "squareCover";
    public const string SongTitle = "songTitle";
    public const string MapBackground = "mapBackground";
    public const string AlbumCoach = "albumCoach";
    public const string Banner = "banner";

    public static string Coach(int coachNumber) => $"coach{coachNumber:D2}";
}

public sealed record OnlineAssetDefinition(string Key, string AssetType, string RelativePath);

public sealed record OnlineAssetAvailabilityEntry(OnlineAssetDefinition Definition, Uri? Uri)
{
    public bool IsAvailable => Uri is not null;
}

public sealed class OnlineAssetAvailability(IEnumerable<OnlineAssetAvailabilityEntry> entries)
{
    private readonly Dictionary<string, OnlineAssetAvailabilityEntry> _entries = entries
        .ToDictionary(entry => entry.Definition.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<OnlineAssetAvailabilityEntry> Entries => _entries.Values;

    public bool Has(string key) =>
        _entries.TryGetValue(key, out OnlineAssetAvailabilityEntry? entry) && entry.IsAvailable;

    public Uri? GetUri(string key) =>
        _entries.TryGetValue(key, out OnlineAssetAvailabilityEntry? entry) ? entry.Uri : null;
}

public sealed record OnlineAssetDownloadResult(OnlineAssetDefinition Definition, bool Downloaded, string DestinationPath);

/// <summary>
/// Queries and downloads online JDI assets from the shared cover repository.
/// </summary>
public class OnlineAssetDownloader(ILogger? logger = null)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/MrKev312/JustDanceCovers/refs/heads/main/";
    private const string CoversJsonUrl = $"{BaseUrl}Covers.json";
    private static readonly HttpClient HttpClient = new();

    public static IReadOnlyList<OnlineAssetDefinition> CoverAssetDefinitions { get; } =
    [
        new(OnlineAssetKeys.Cover, "Cover", IntermediatePackageLayout.Assets.CoverFile),
        new(OnlineAssetKeys.SquareCover, "Square", IntermediatePackageLayout.Assets.SquareCoverFile),
        new(OnlineAssetKeys.MapBackground, "Background", IntermediatePackageLayout.Assets.MapBackgroundFile),
        new(OnlineAssetKeys.AlbumCoach, "AlbumCoach", IntermediatePackageLayout.Assets.AlbumCoachFile)
    ];

    public static IReadOnlyList<OnlineAssetDefinition> GetDefaultAssetDefinitions(IntermediateSongPackage package)
    {
        List<OnlineAssetDefinition> definitions =
        [
            new(OnlineAssetKeys.Cover, "Cover", IntermediatePackageLayout.Assets.CoverFile),
            new(OnlineAssetKeys.SongTitle, "Title", IntermediatePackageLayout.Assets.SongTitleFile),
            new(OnlineAssetKeys.SquareCover, "Square", IntermediatePackageLayout.Assets.SquareCoverFile),
            new(OnlineAssetKeys.MapBackground, "Background", IntermediatePackageLayout.Assets.MapBackgroundFile),
            new(OnlineAssetKeys.AlbumCoach, "AlbumCoach", IntermediatePackageLayout.Assets.AlbumCoachFile),
            new(OnlineAssetKeys.Banner, "Banner", IntermediatePackageLayout.Assets.BannerFile)
        ];

        int coachCount = Math.Min(package.Metadata.CoachCount, 6);
        for (int coachNumber = 1; coachNumber <= coachCount; coachNumber++)
        {
            definitions.Add(new(
                OnlineAssetKeys.Coach(coachNumber),
                $"Coach_{coachNumber}",
                IntermediatePackageLayout.Assets.CoachFile(coachNumber)));
        }

        return definitions;
    }

    /// <summary>
    /// Downloads all default online assets for a package. CLI callers keep this broad behavior.
    /// </summary>
    public async Task DownloadAssetsAsync(
        string packageRoot,
        IntermediateSongPackage package,
        IFileSystem? io = null,
        Func<string, string>? mapRelativeAssetPath = null,
        CancellationToken cancellationToken = default)
    {
        await DownloadAssetsAsync(
            packageRoot,
            package,
            GetDefaultAssetDefinitions(package),
            io,
            mapRelativeAssetPath,
            availability: null,
            cancellationToken);
    }

    /// <summary>
    /// Downloads only the requested online assets that actually exist.
    /// </summary>
    public async Task<IReadOnlyList<OnlineAssetDownloadResult>> DownloadAssetsAsync(
        string packageRoot,
        IntermediateSongPackage package,
        IEnumerable<OnlineAssetDefinition> assets,
        IFileSystem? io = null,
        Func<string, string>? mapRelativeAssetPath = null,
        OnlineAssetAvailability? availability = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        IFileSystem iofs = io ?? new SystemFileSystem();
        OnlineAssetDefinition[] requestedAssets = [.. assets.DistinctBy(asset => asset.Key)];
        if (requestedAssets.Length == 0)
            return [];

        string songName = package.Metadata.MapName;
        logger?.LogInformation(
            "Requesting {AssetCount} online asset(s) for '{SongName}'",
            requestedAssets.Length,
            songName);

        availability ??= await GetAvailabilityAsync(songName, requestedAssets, cancellationToken);
        Task<OnlineAssetDownloadResult>[] downloadTasks = requestedAssets
            .Select(asset => DownloadAssetIfAvailableAsync(packageRoot, asset, availability, iofs, mapRelativeAssetPath, cancellationToken))
            .ToArray();

        OnlineAssetDownloadResult[] results = await Task.WhenAll(downloadTasks);
        int downloadedCount = results.Count(result => result.Downloaded);
        logger?.LogInformation(
            "Downloaded {DownloadedCount}/{AssetCount} requested online asset(s) for '{SongName}'",
            downloadedCount,
            requestedAssets.Length,
            songName);

        return results;
    }

    public async Task<OnlineAssetAvailability> GetAvailabilityAsync(
        string mapName,
        IEnumerable<OnlineAssetDefinition> assets,
        CancellationToken cancellationToken = default)
    {
        OnlineAssetDefinition[] requestedAssets = [.. assets.DistinctBy(asset => asset.Key)];
        Lazy<Task<string?>> codenameLookup = new(() => ResolveCodenameAsync(mapName, cancellationToken));

        Task<OnlineAssetAvailabilityEntry>[] probes = requestedAssets
            .Select(asset => ProbeAssetAsync(mapName, asset, codenameLookup, cancellationToken))
            .ToArray();

        return new OnlineAssetAvailability(await Task.WhenAll(probes));
    }

    private async Task<OnlineAssetAvailabilityEntry> ProbeAssetAsync(
        string mapName,
        OnlineAssetDefinition asset,
        Lazy<Task<string?>> codenameLookup,
        CancellationToken cancellationToken)
    {
        Uri directUri = BuildAssetUri(mapName, asset.AssetType);
        if (await AssetExistsAsync(directUri, cancellationToken))
            return new OnlineAssetAvailabilityEntry(asset, directUri);

        string? codename = await codenameLookup.Value;
        if (!string.IsNullOrWhiteSpace(codename) && !codename.Equals(mapName, StringComparison.OrdinalIgnoreCase))
        {
            Uri codenameUri = BuildAssetUri(codename, asset.AssetType);
            if (await AssetExistsAsync(codenameUri, cancellationToken))
                return new OnlineAssetAvailabilityEntry(asset, codenameUri);
        }

        return new OnlineAssetAvailabilityEntry(asset, Uri: null);
    }

    private async Task<OnlineAssetDownloadResult> DownloadAssetIfAvailableAsync(
        string packageRoot,
        OnlineAssetDefinition asset,
        OnlineAssetAvailability availability,
        IFileSystem io,
        Func<string, string>? mapRelativeAssetPath,
        CancellationToken cancellationToken)
    {
        string relativePath = mapRelativeAssetPath?.Invoke(asset.RelativePath) ?? asset.RelativePath;
        string destination = Path.Combine(packageRoot, relativePath);
        Uri? uri = availability.GetUri(asset.Key);
        if (uri is null)
            return new OnlineAssetDownloadResult(asset, Downloaded: false, destination);

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new OnlineAssetDownloadResult(asset, Downloaded: false, destination);

            await SaveAssetFromResponseAsync(destination, response, io, cancellationToken);
            logger?.LogInformation("Downloaded online asset '{AssetType}' to '{Destination}'", asset.AssetType, destination);
            return new OnlineAssetDownloadResult(asset, Downloaded: true, destination);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(
                "Failed to download online asset '{AssetType}' to '{Destination}': {Message}",
                asset.AssetType,
                destination,
                ex.Message);
            return new OnlineAssetDownloadResult(asset, Downloaded: false, destination);
        }
    }

    private static async Task<bool> AssetExistsAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, uri);
            using HttpResponseMessage response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
                return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task SaveAssetFromResponseAsync(
        string fullPath,
        HttpResponseMessage response,
        IFileSystem io,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            io.CreateDirectory(directory);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream fileStream = File.Open(fullPath, FileMode.Create, FileAccess.Write);
        await contentStream.CopyToAsync(fileStream, cancellationToken);
    }

    private async Task<string?> ResolveCodenameAsync(string mapName, CancellationToken cancellationToken)
    {
        try
        {
            string json = await HttpClient.GetStringAsync(CoversJsonUrl, cancellationToken);
            Dictionary<string, string[]>? covers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            if (covers is null)
                return null;

            return covers
                .Where(pair => pair.Value?.Contains(mapName, StringComparer.OrdinalIgnoreCase) ?? false)
                .Select(pair => pair.Key)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogDebug("Failed to resolve codename for '{MapName}': {Message}", mapName, ex.Message);
            return null;
        }
    }

    private static Uri BuildAssetUri(string mapName, string assetType)
    {
        string escapedMapName = Uri.EscapeDataString(mapName).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        string escapedAssetType = Uri.EscapeDataString(assetType);
        return new Uri($"{BaseUrl}Covers/{escapedMapName}/{escapedAssetType}.webp");
    }
}