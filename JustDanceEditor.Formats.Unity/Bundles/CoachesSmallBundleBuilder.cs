using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.Unity.Images;

using KevInc.Texture;
using KevInc.Texture.ImageSharp;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoachesSmallRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer,
    UnityBundlePublishTarget? PublishTarget = null);

public sealed class CoachesSmallBundleBuilder(ILogger logger) : UnityBundleBuilderBase
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
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        List<Image<Rgba32>> coachImages = [];
        try
        {
            LoadCoachImages(request, coachImages);

            BundleContext internalRequest = new(
                request.SongName,
                coachImages,
                request.TemplatePath,
                request.OutputFolderPath,
                request.ForCustomServer,
                request.PublishTarget);

            GenerateBundle(internalRequest);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
        }
    }

    private void GenerateBundle(BundleContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBundleRequest(request);

        _logger.LogInformation("Converting CoachesSmall bundle for {Codename}...", request.Codename);
        try
        {
            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, AssetFileInfo? assetBundleInfo, AssetTypeValueField? assetBundleBase, AssetFileInfo? textureInfo, AssetFileInfo? spriteInfo) =
                InitializeBundle(request.TemplatePath, request.Codename);

            // Set coaches-specific bundle names
            if (assetBundleBase != null)
            {
                assetBundleBase["m_Name"].AsString = $"{request.Codename}_CoachesSmall";
                assetBundleBase["m_AssetBundleName"].AsString = $"{request.Codename}_CoachesSmall";
            }

            if (assetBundleBase == null)
                throw new InvalidOperationException("Asset bundle base not found in template bundle.");

            List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

            (AssetFileInfo? coachTextureTpl, AssetFileInfo? coachSpriteTpl, long[]? textureIds, long[]? spriteIds) = ClearBundleAndIdentifyTemplates(request, manager, afileInst, afile, assetBundleBase);

            ProcessCoachAssets(request, manager, afileInst, afile, coachTextureTpl, coachSpriteTpl, textureIds, spriteIds);
            PopulatePreloadTable(assetBundleBase["m_PreloadTable"]["Array"], textureIds, spriteIds);
            PopulateAssetContainer(request, assetBundleBase["m_Container"]["Array"], textureIds, spriteIds);

            AssetFileInfo assetBundleInfoFinal = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);

            if (assetBundleBase == null)
                throw new InvalidOperationException("Asset bundle base not found in template bundle.");

            FinalizeAndSaveBundle(request.OutputFolderPath, request.ForCustomServer, bunInst.file, afile, assetBundleBase, assetBundleInfoFinal.SetNewData, request.PublishTarget);
            _logger.LogInformation("Finished CoachesSmall bundle for {Codename}", request.Codename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate CoachesSmall bundle for {Codename}", request.Codename);
            throw;
        }
    }

    private static void ValidateInput(UnityCoachesSmallRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.MenuArt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
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

    private static void ValidateBundleRequest(BundleContext request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (request.CoachImages == null || request.CoachImages.Count == 0)
            throw new ArgumentException("At least one coach image is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath) || !File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
    }

    private static (AssetFileInfo coachTexture, AssetFileInfo coachSprite, long[] textureIds, long[] spriteIds)
        ClearBundleAndIdentifyTemplates(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleBase)
    {
        AssetFileInfo? coachTexture = null;
        AssetFileInfo? coachSprite = null;
        int coachCount = request.CoachImages.Count;
        long[] textureIds = new long[coachCount];
        long[] spriteIds = new long[coachCount];

        AssetTypeValueField preloadArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField containerArray = assetBundleBase["m_Container"]["Array"];
        preloadArray.Children.Clear();
        containerArray.Children.Clear();

        List<AssetFileInfo> assetsToRemove = [];
        foreach (AssetFileInfo assetInfo in afile.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1_Phone", StringComparison.Ordinal))
            {
                coachTexture = assetInfo;
                textureIds[0] = assetInfo.PathId;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetFileInfo assetInfo in assetsToRemove)
            afile.AssetInfos.Remove(assetInfo);
        assetsToRemove.Clear();

        foreach (AssetFileInfo assetInfo in afile.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1_Phone", StringComparison.Ordinal))
            {
                coachSprite = assetInfo;
                spriteIds[0] = assetInfo.PathId;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetFileInfo assetInfo in assetsToRemove)
            afile.AssetInfos.Remove(assetInfo);

        if (coachTexture == null || coachSprite == null)
            throw new InvalidOperationException("Failed to locate required template assets for CoachesSmall bundle.");

        return (coachTexture, coachSprite, textureIds, spriteIds);
    }

    private static void ProcessCoachAssets(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile,
        AssetFileInfo coachTextureTpl, AssetFileInfo coachSpriteTpl, long[] textureIds, long[] spriteIds)
    {
        TextureFormat fmt = TextureFormat.DXT5Crunched;

        for (int i = 1; i <= request.CoachImages.Count; i++)
        {
            long coachTextureId = i == 1 ? textureIds[0] : afile.GetRandomId();
            long coachSpriteId = i == 1 ? spriteIds[0] : afile.GetRandomId();

            AssetTypeValueField coachTextureBase = manager.GetBaseField(afileInst, coachTextureTpl);
            AssetTypeValueField coachSpriteBase = manager.GetBaseField(afileInst, coachSpriteTpl);

            coachTextureBase["m_Name"].AsString = $"{request.Codename}_Coach_{i}_Phone";
            coachSpriteBase["m_Name"].AsString = $"{request.Codename}_Coach_{i}_Phone";

            using Image<Rgba32> image = request.CoachImages[i - 1].CloneAs<Rgba32>();
            image.Mutate(x => x.Resize(256, 256));
            image.Mutate(x => x.Flip(FlipMode.Vertical));

            byte[] encImageBytes = TextureImageSharpCodec.EncodeData(image, fmt, quality: 5, mipCount: 1);

            coachTextureBase["image data"].AsByteArray = encImageBytes;
            coachTextureBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coachTextureBase["m_StreamData"]["offset"].AsULong = 0;
            coachTextureBase["m_StreamData"]["size"].AsUInt = 0;
            coachTextureBase["m_StreamData"]["path"].AsString = string.Empty;

            if (i == 1)
            {
                coachTextureTpl.SetNewData(coachTextureBase);
                coachSpriteTpl.SetNewData(coachSpriteBase);
            }
            else
            {
                uint[] renderKey = Guid.NewGuid().ToUnity();
                coachSpriteBase["m_RenderDataKey"]["first"]["data[0]"].AsUInt = renderKey[0];
                coachSpriteBase["m_RenderDataKey"]["first"]["data[1]"].AsUInt = renderKey[1];
                coachSpriteBase["m_RenderDataKey"]["first"]["data[2]"].AsUInt = renderKey[2];
                coachSpriteBase["m_RenderDataKey"]["first"]["data[3]"].AsUInt = renderKey[3];
                coachSpriteBase["m_RD"]["texture"]["m_PathID"].AsLong = coachTextureId;

                AssetFileInfo newTextureInfo = AssetFileInfo.Create(afile, coachTextureId, (int)AssetClassID.Texture2D, null);
                AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, coachSpriteId, (int)AssetClassID.Sprite, null);
                newTextureInfo.SetNewData(coachTextureBase);
                newSpriteInfo.SetNewData(coachSpriteBase);

                afile.Metadata.AddAssetInfo(newTextureInfo);
                afile.Metadata.AddAssetInfo(newSpriteInfo);
            }

            textureIds[i - 1] = coachTextureId;
            spriteIds[i - 1] = coachSpriteId;
        }
    }

    private static void PopulatePreloadTable(AssetTypeValueField preloadArray, long[] textureIds, long[] spriteIds)
    {
        foreach (long id in textureIds.Union(spriteIds))
        {
            AssetTypeValueField entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadArray);
            entry["m_PathID"].AsLong = id;
            preloadArray.Children.Add(entry);
        }
    }

    private static void PopulateAssetContainer(BundleContext request, AssetTypeValueField containerArray, long[] textureIds, long[] spriteIds)
    {
        for (int i = 0; i < request.CoachImages.Count; i++)
        {
            string name = $"Coach{i + 1}_Phone";

            AssetTypeValueField textureEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(containerArray);
            textureEntry["first"].AsString = name;
            textureEntry["second"]["preloadIndex"].AsInt = 2 * i;
            textureEntry["second"]["preloadSize"].AsInt = 2;
            textureEntry["second"]["asset"]["m_PathID"].AsLong = textureIds[i];
            containerArray.Children.Add(textureEntry);

            AssetTypeValueField spriteEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(containerArray);
            spriteEntry["first"].AsString = name;
            spriteEntry["second"]["preloadIndex"].AsInt = 2 * i;
            spriteEntry["second"]["preloadSize"].AsInt = 2;
            spriteEntry["second"]["asset"]["m_PathID"].AsLong = spriteIds[i];
            containerArray.Children.Add(spriteEntry);
        }
    }

    private sealed record BundleContext(
        string Codename,
        IReadOnlyList<Image<Rgba32>> CoachImages,
        string TemplatePath,
        string OutputFolderPath,
        bool ForCustomServer,
        UnityBundlePublishTarget? PublishTarget);
}
