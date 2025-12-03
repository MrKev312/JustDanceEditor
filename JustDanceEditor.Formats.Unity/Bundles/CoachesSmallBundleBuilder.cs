using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoachesSmallBundleRequest(
    string Codename,
    IReadOnlyList<Image<Rgba32>> CoachImages,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer);

public static class CoachesSmallBundleBuilder
{
    public static void Generate(UnityCoachesSmallBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        Logger.Log($"Converting CoachesSmall bundle for {request.Codename}...");
        try
        {
            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, List<AssetFileInfo>? sortedAssetInfos, AssetTypeValueField? assetBundleBase) = InitializeBundle(request);
            (AssetFileInfo? coachTextureTpl, AssetFileInfo? coachSpriteTpl, long[]? textureIds, long[]? spriteIds) = ClearBundleAndIdentifyTemplates(request, manager, afileInst, afile, assetBundleBase);

            ProcessCoachAssets(request, manager, afileInst, afile, coachTextureTpl, coachSpriteTpl, textureIds, spriteIds);
            PopulatePreloadTable(assetBundleBase["m_PreloadTable"]["Array"], textureIds, spriteIds);
            PopulateAssetContainer(request, assetBundleBase["m_Container"]["Array"], textureIds, spriteIds);

            AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
            FinalizeAndSaveBundle(request, bunInst.file, afile, assetBundleBase, assetBundleInfo.SetNewData);
            Logger.Log($"Finished CoachesSmall bundle for {request.Codename}");
        }
        catch
        {
            Logger.Log($"Failed to generate CoachesSmall bundle for {request.Codename}", LogLevel.Error);
            throw;
        }
    }

    private static void ValidateRequest(UnityCoachesSmallBundleRequest request)
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

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, List<AssetFileInfo> SortedAssetInfos, AssetTypeValueField AssetBundleBase)
        InitializeBundle(UnityCoachesSmallBundleRequest request)
    {
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(request.TemplatePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{request.Codename}_CoachesSmall";
        assetBundleBase["m_AssetBundleName"].AsString = $"{request.Codename}_CoachesSmall";

        return (manager, bunInst, afileInst, afile, sortedAssetInfos, assetBundleBase);
    }

    private static (AssetFileInfo coachTexture, AssetFileInfo coachSprite, long[] textureIds, long[] spriteIds)
        ClearBundleAndIdentifyTemplates(UnityCoachesSmallBundleRequest request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleBase)
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

    private static void ProcessCoachAssets(UnityCoachesSmallBundleRequest request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile,
        AssetFileInfo coachTextureTpl, AssetFileInfo coachSpriteTpl, long[] textureIds, long[] spriteIds)
    {
        TextureFormat fmt = TextureFormat.DXT5Crunched;
        int mips = 1;

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

            byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new InvalidOperationException("Failed to encode coach phone image.");

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

    private static void PopulateAssetContainer(UnityCoachesSmallBundleRequest request, AssetTypeValueField containerArray, long[] textureIds, long[] spriteIds)
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

    private static void FinalizeAndSaveBundle(UnityCoachesSmallBundleRequest request, AssetBundleFile bundle, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bundle.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bundle.SaveAndCompress(request.OutputFolderPath, request.ForCustomServer);
    }
}
