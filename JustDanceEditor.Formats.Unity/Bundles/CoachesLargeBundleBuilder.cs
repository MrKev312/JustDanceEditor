using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using KevInc.Texture;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoachesLargeRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    UnityExportData UnityData,
    string OutputFolderPath,
    bool ForCustomServer,
    UnityBundlePublishTarget? PublishTarget = null);

public sealed class CoachesLargeBundleBuilder(ILogger logger)
{
    private readonly ILogger _logger = logger;

    public static Task GenerateAsync(UnityCoachesLargeRequest request, ILogger logger) =>
        Task.Run(() => Generate(request, logger));

    public static void Generate(UnityCoachesLargeRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        CoachesLargeBundleBuilder builder = new(logger);
        builder.Run(request);
    }

    private void Run(UnityCoachesLargeRequest request)
    {
        ValidateInput(request);

        List<Image<Rgba32>> coachImages = [];
        Image<Rgba32>? background = null;

        try
        {
            LoadCoachImages(request, coachImages);
            background = ImageLoader.TryLoadImage(request.MenuArt.CoachesBackgroundPath) ?? CreateFallbackBackground();

            UnityImageBundleGenerator.Generate(new UnityImageBundleRequest(
                $"{request.SongName}_CoachesLarge",
                request.OutputFolderPath,
                request.ForCustomServer,
                BuildAssets(request.SongName, background, coachImages),
                request.PublishTarget));

            _logger.LogInformation("Finished CoachesLarge bundle for {Codename}", request.SongName);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
            background?.Dispose();
        }
    }

    private static void ValidateInput(UnityCoachesLargeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.MenuArt);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
    }

    private static void LoadCoachImages(UnityCoachesLargeRequest request, List<Image<Rgba32>> destination)
    {
        IReadOnlyList<string> coachFiles = request.MenuArt.CoachImagePaths;
        if (coachFiles.Count == 0)
            throw new FileNotFoundException("No coach images defined in the intermediate package.");

        for (int i = 0; i < request.CoachCount; i++)
        {
            if (i >= coachFiles.Count)
                throw new InvalidOperationException($"Not enough coach images available. Needed {request.CoachCount}, found {coachFiles.Count}.");

            destination.Add(Image.Load<Rgba32>(coachFiles[i]));
        }
    }

    private static Image<Rgba32> CreateFallbackBackground() => new(2048, 1024, Color.Magenta);

    private static IReadOnlyList<UnityImageBundleAsset> BuildAssets(string songName, Image<Rgba32> background, IReadOnlyList<Image<Rgba32>> coachImages)
    {
        UnityImageBundleAsset[] assets = new UnityImageBundleAsset[coachImages.Count + 1];
        assets[0] = new UnityImageBundleAsset(
            "CoachesBackground",
            $"{songName}_map_bkg",
            $"{songName}_map_bkg",
            background,
            2048,
            1024,
            TextureFormat.DXT1Crunched);

        for (int i = 0; i < coachImages.Count; i++)
        {
            string assetName = $"{songName}_Coach_{i + 1}";
            assets[i + 1] = new UnityImageBundleAsset(
                $"Coach{i + 1}",
                assetName,
                assetName,
                coachImages[i],
                1024,
                1024,
                TextureFormat.DXT5Crunched);
        }

        return assets;
    }
}
