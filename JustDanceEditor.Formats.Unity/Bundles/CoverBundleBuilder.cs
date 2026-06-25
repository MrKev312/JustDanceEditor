using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using KevInc.Texture;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoverRequest(
    string SongName,
    UnityExportData? UnityData,
    UnityMenuArtSource? MenuArt,
    string OutputFolderPath,
    bool ForCustomServer,
    Image<Rgba32>? OverrideCoverImage = null,
    UnityBundlePublishTarget? PublishTarget = null);

public sealed class CoverBundleBuilder(ILogger logger)
{
    private readonly ILogger _logger = logger;

    public static Task GenerateAsync(UnityCoverRequest request, ILogger logger) =>
        Task.Run(() => Generate(request, logger));

    public static void Generate(UnityCoverRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        CoverBundleBuilder builder = new(logger);
        builder.Run(request);
    }

    private void Run(UnityCoverRequest request)
    {
        ValidateInput(request);

        using Image<Rgba32>? coverImage = PrepareCoverImage(request);
        if (coverImage == null)
        {
            _logger.LogWarning("Cover image could not be prepared, skipping cover bundle generation.");
            return;
        }

        coverImage.Mutate(ctx => ctx.Resize(640, 360));
        UnityImageBundleGenerator.Generate(new UnityImageBundleRequest(
            $"{request.SongName}_Cover",
            request.OutputFolderPath,
            request.ForCustomServer,
            [
                new UnityImageBundleAsset(
                    "Cover",
                    $"{request.SongName}_Cover_2x",
                    $"{request.SongName}_Cover_2x",
                    coverImage,
                    640,
                    360,
                    TextureFormat.DXT1Crunched)
            ],
            request.PublishTarget));
    }

    private static void ValidateInput(UnityCoverRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
        if (request.OverrideCoverImage == null && (request.UnityData == null || request.MenuArt == null))
            throw new ArgumentException("Either an override cover image or Unity data with menu art must be provided.");
    }

    private static Image<Rgba32>? PrepareCoverImage(UnityCoverRequest request)
    {
        if (request.OverrideCoverImage is not null)
            return request.OverrideCoverImage.CloneAs<Rgba32>();

        if (request.UnityData == null || request.MenuArt == null)
            return null;

        return ImageLoader.TryLoadImage(request.MenuArt.CoverPath);
    }
}