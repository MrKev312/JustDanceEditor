using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoverRequest(
    string SongName,
    UnityExportData? UnityData,
    UnityMenuArtSource? MenuArt,
    bool AllowOnlineLookup,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer,
    Image<Rgba32>? OverrideCoverImage = null);

public sealed class CoverBundleBuilder(ILogger<CoverBundleBuilder> logger) : UnityBundleBuilderBase
{
    private readonly ILogger<CoverBundleBuilder> _logger = logger;

    public static Task GenerateAsync(UnityCoverRequest request, ILogger logger) =>
        Task.Run(() => Generate(request, logger));

    public static void Generate(UnityCoverRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        CoverBundleBuilder builder = new(logger as ILogger<CoverBundleBuilder> ?? throw new ArgumentNullException(nameof(logger)));
        builder.Run(request);
    }

    private void Run(UnityCoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        using Image<Rgba32>? coverImage = PrepareCoverImage(request);
        if (coverImage == null)
        {
            _logger.LogWarning("Cover image could not be prepared, skipping cover bundle generation.");
            return;
        }

        coverImage.Mutate(x => x.Resize(640, 360));

        BundleContext internalRequest = new(
            request.SongName,
            coverImage,
            request.TemplatePath,
            request.OutputFolderPath,
            request.ForCustomServer);

        GenerateBundle(internalRequest);
    }

    private void GenerateBundle(BundleContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBundleRequest(request);

        try
        {
            _logger.LogInformation("Starting generation for cover: {Codename}", request.Codename);

            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, AssetFileInfo? assetBundleInfo, AssetTypeValueField? assetBundleBase, AssetFileInfo? coverTextureInfo, AssetFileInfo? coverSpriteInfo) =
                base.InitializeBundle(request.TemplatePath, request.Codename);

            // Set cover-specific names on the asset bundle base
            if (assetBundleBase != null)
            {
                assetBundleBase["m_Name"].AsString = $"{request.Codename}_Cover";
                assetBundleBase["m_AssetBundleName"].AsString = $"{request.Codename}_Cover";
            }

            UpdateCoverTexture(request.Codename, manager, afileInst, coverTextureInfo, request.CoverImage);
            UpdateCoverSprite(request.Codename, manager, afileInst, coverSpriteInfo);

            // Ensure asset bundle base is available
            if (assetBundleBase == null)
                throw new InvalidOperationException("Asset bundle base not found in template bundle.");

            // Use the base finalizer to commit changes and save the bundle
            FinalizeAndSaveBundle(request.OutputFolderPath, request.ForCustomServer, bunInst.file, afile, assetBundleBase, assetBundleInfo.SetNewData);

            _logger.LogInformation("Finished generating cover for {Codename}", request.Codename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cover for {Codename}: {Message}", request.Codename, ex.Message);
            throw;
        }
    }

    private static void ValidateInput(UnityCoverRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);
        if (request.OverrideCoverImage == null && (request.UnityData == null || request.MenuArt == null))
            throw new ArgumentException("Either an override cover image or Unity data with menu art must be provided.");
    }

    private static void ValidateBundleRequest(BundleContext request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (request.CoverImage == null)
            throw new ArgumentException("Cover image must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new ArgumentException("Template path must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
        if (!File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
    }

    private Image<Rgba32>? PrepareCoverImage(UnityCoverRequest request)
    {
        if (request.OverrideCoverImage is not null)
            return request.OverrideCoverImage.CloneAs<Rgba32>();

        if (request.UnityData == null || request.MenuArt == null)
            return null;

        Image<Rgba32>? image = null;
        if (request.AllowOnlineLookup)
            image = ImageLoader.TryImageWeb(request.SongName, "Cover", _logger);

        image ??= ImageLoader.TryLoadImage(request.MenuArt.CoverPath);
        if (image != null)
            _logger.LogDebug("Cover image prepared from intermediate assets.");

        return image;
    }

    private static void UpdateCoverTexture(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverInfo, Image<Rgba32> coverImage)
    {
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);
        coverBase["m_Name"].AsString = $"{codename}_Cover_2x";

        TextureFormat fmt = TextureFormat.DXT1Crunched;
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(coverImage, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode cover image!");

        coverBase["image data"].AsByteArray = encImageBytes;
        coverBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        coverBase["m_StreamData"]["offset"].AsInt = 0;
        coverBase["m_StreamData"]["size"].AsInt = 0;
        coverBase["m_StreamData"]["path"].AsString = string.Empty;

        coverInfo.SetNewData(coverBase);
    }

    private static void UpdateCoverSprite(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverSpriteInfo)
    {
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);
        coverSpriteBase["m_Name"].AsString = $"{codename}_Cover_2x";
        coverSpriteInfo.SetNewData(coverSpriteBase);
    }

    private sealed record BundleContext(
        string Codename,
        Image<Rgba32> CoverImage,
        string TemplatePath,
        string OutputFolderPath,
        bool ForCustomServer);
}