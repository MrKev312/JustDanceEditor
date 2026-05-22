using JustDanceEditor.Formats.Unity.Images;

using KevInc.Texture;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoachesSmallRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    string OutputFolderPath,
    bool ForCustomServer,
    UnityBundlePublishTarget? PublishTarget = null);

public sealed class CoachesSmallBundleBuilder(ILogger logger)
{
    private readonly ILogger _logger = logger;

    public static Task GenerateAsync(UnityCoachesSmallRequest request, ILogger logger) =>
        Task.Run(() => Generate(request, logger));

    public static void Generate(UnityCoachesSmallRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        CoachesSmallBundleBuilder builder = new(logger);
        builder.Run(request);
    }

    private void Run(UnityCoachesSmallRequest request)
    {
        ValidateInput(request);

        List<Image<Rgba32>> coachImages = [];
        try
        {
            LoadCoachImages(request, coachImages);
            UnityImageBundleGenerator.Generate(new UnityImageBundleRequest(
                $"{request.SongName}_CoachesSmall",
                request.OutputFolderPath,
                request.ForCustomServer,
                BuildAssets(request.SongName, coachImages),
                request.PublishTarget));

            _logger.LogInformation("Finished CoachesSmall bundle for {Codename}", request.SongName);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
        }
    }

    private static void ValidateInput(UnityCoachesSmallRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.MenuArt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
    }

    private static void LoadCoachImages(UnityCoachesSmallRequest request, List<Image<Rgba32>> destination)
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

    private static IReadOnlyList<UnityImageBundleAsset> BuildAssets(string songName, IReadOnlyList<Image<Rgba32>> coachImages)
    {
        UnityImageBundleAsset[] assets = new UnityImageBundleAsset[coachImages.Count];
        for (int i = 0; i < coachImages.Count; i++)
        {
            string assetName = $"{songName}_Coach_{i + 1}_Phone";
            assets[i] = new UnityImageBundleAsset(
                $"Coach{i + 1}",
                assetName,
                assetName,
                coachImages[i],
                256,
                256,
                TextureFormat.DXT5Crunched);
        }

        return assets;
    }
}
