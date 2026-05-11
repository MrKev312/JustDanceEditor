using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using KevInc.Texture;
using KevInc.Texture.ImageSharp;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnitySongTitleRequest(
    string SongName,
    UnityExportData? UnityData,
    UnityMenuArtSource? MenuArt,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer,
    Image<Rgba32>? OverrideTitleImage = null);

public sealed class SongTitleBundleBuilder : UnityBundleBuilderBase
{
    public static Task GenerateAsync(UnitySongTitleRequest request, ILogger logger) =>
        Task.Run(() => new SongTitleBundleBuilder().Run(request, logger));

    // Backwards-compatibility wrapper for callers using the previous static API
    public static void Generate(UnitySongTitleRequest request, ILogger logger) => new SongTitleBundleBuilder().Run(request, logger);

    private void Run(UnitySongTitleRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        using Image<Rgba32>? titleImage = PrepareSongTitleImage(request, logger);
        if (titleImage == null)
        {
            logger.LogInformation("No song title logo image found, skipping song title bundle generation.");
            return;
        }

        NormalizeSongTitleImage(titleImage);

        BundleContext internalRequest = new(
            request.SongName,
            titleImage,
            request.TemplatePath,
            request.OutputFolderPath,
            request.ForCustomServer);

        GenerateBundle(internalRequest, logger);
    }

    private void GenerateBundle(BundleContext request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBundleRequest(request);

        AssetsManager? manager = null;
        try
        {
            logger.LogInformation("Starting generation for song title logo: {Codename}", request.Codename);

            (manager, BundleFileInstance bunInst, AssetsFileInstance afileInst, AssetsFile afile, AssetFileInfo assetBundleInfo, AssetTypeValueField assetBundleBase, AssetFileInfo textureInfo, AssetFileInfo spriteInfo) =
                InitializeBundle(request.TemplatePath, request.Codename);

            UpdateSongTitleTexture(request.Codename, manager, afileInst, textureInfo, request.TitleImage);
            UpdateSongTitleSprite(request.Codename, manager, afileInst, spriteInfo);

            FinalizeAndSaveBundle(request.OutputFolderPath, request.ForCustomServer, bunInst.file, afile, assetBundleBase, assetBundleInfo.SetNewData);

            logger.LogInformation("Finished generating song title logo for {Codename}", request.Codename);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate song title logo for {Codename}: {Message}", request.Codename, ex.Message);
            throw;
        }
        finally
        {
            ClearBundle(manager);
        }
    }

    private static void ValidateInput(UnitySongTitleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
        if (request.OverrideTitleImage == null && (request.UnityData == null || request.MenuArt == null))
            throw new ArgumentException("Either an override song title image or Unity data with menu art must be provided.");
    }

    private static void ValidateBundleRequest(BundleContext request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (request.TitleImage == null)
            throw new ArgumentException("Title image must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new ArgumentException("Template path must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
        if (!File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
    }

    private static Image<Rgba32>? PrepareSongTitleImage(UnitySongTitleRequest request, ILogger logger)
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
            image.Mutate(x => x.Pad(newWidth, image.Height));
        }

        image.Mutate(x => x.Resize(1024, 512));
    }

    private static void UpdateSongTitleTexture(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo textureInfo, Image<Rgba32> image)
    {
        AssetTypeValueField textureBase = manager.GetBaseField(afileInst, textureInfo);
        textureBase["m_Name"].AsString = $"{codename}_Title";

        TextureFormat fmt = TextureFormat.DXT5Crunched;
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        byte[] encImageBytes = TextureImageSharpCodec.EncodeData(image, fmt, quality: 5, mipCount: 1);

        textureBase["image data"].AsByteArray = encImageBytes;
        textureBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        textureBase["m_StreamData"]["offset"].AsInt = 0;
        textureBase["m_StreamData"]["size"].AsInt = 0;
        textureBase["m_StreamData"]["path"].AsString = string.Empty;

        textureInfo.SetNewData(textureBase);
    }

    private static void UpdateSongTitleSprite(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo spriteInfo)
    {
        AssetTypeValueField spriteBase = manager.GetBaseField(afileInst, spriteInfo);
        spriteBase["m_Name"].AsString = $"{codename}_Title";
        spriteInfo.SetNewData(spriteBase);
    }

    private sealed record BundleContext(
        string Codename,
        Image<Rgba32> TitleImage,
        string TemplatePath,
        string OutputFolderPath,
        bool ForCustomServer);
}
