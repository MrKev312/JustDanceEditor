using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using KevInc.Texture;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnitySongTitleRequest(
    string SongName,
    UnityExportData? UnityData,
    UnityMenuArtSource? MenuArt,
    string OutputFolderPath,
    bool ForCustomServer,
    Image<Rgba32>? OverrideTitleImage = null,
    UnityBundlePublishTarget? PublishTarget = null);

public sealed class SongTitleBundleBuilder
{
    public static Task GenerateAsync(UnitySongTitleRequest request, ILogger logger) =>
        Task.Run(() => Generate(request, logger));

    public static void Generate(UnitySongTitleRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        using Image<Rgba32>? titleImage = PrepareSongTitleImage(request);
        if (titleImage == null)
        {
            logger.LogInformation("No song title logo image found, skipping song title bundle generation.");
            return;
        }

        NormalizeSongTitleImage(titleImage);
        UnityImageBundleGenerator.Generate(new UnityImageBundleRequest(
            $"{request.SongName}_SongTitleLogo",
            request.OutputFolderPath,
            request.ForCustomServer,
            [
                new UnityImageBundleAsset(
                    "SongTitleLogo",
                    $"{request.SongName}_Title",
                    $"{request.SongName}_Title",
                    titleImage,
                    1024,
                    512,
                    TextureFormat.DXT5Crunched)
            ],
            request.PublishTarget));

        logger.LogInformation("Finished generating song title logo for {Codename}", request.SongName);
    }

    private static void ValidateInput(UnitySongTitleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
        if (request.OverrideTitleImage == null && (request.UnityData == null || request.MenuArt == null))
            throw new ArgumentException("Either an override song title image or Unity data with menu art must be provided.");
    }

    private static Image<Rgba32>? PrepareSongTitleImage(UnitySongTitleRequest request)
    {
        if (request.OverrideTitleImage is not null)
            return request.OverrideTitleImage.CloneAs<Rgba32>();

        if (request.UnityData == null || request.MenuArt == null)
            return null;

        return ImageLoader.TryLoadImage(request.MenuArt.SongTitleLogoPath);
    }

    private static void NormalizeSongTitleImage(Image<Rgba32> image)
    {
        if (image.Width / (float)image.Height != 2f)
        {
            int newWidth = image.Height * 2;
            image.Mutate(ctx => ctx.Pad(newWidth, image.Height));
        }

        image.Mutate(ctx => ctx.Resize(1024, 512));
    }
}