using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoachesLargeRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    UnityExportData UnityData,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer);

public static class CoachesLargeBundleBuilder
{
    public static Task GenerateAsync(UnityCoachesLargeRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityCoachesLargeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request);

        List<Image<Rgba32>> coachImages = [];
        Image<Rgba32>? background = null;

        try
        {
            LoadCoachImages(request, coachImages);

            UnityCoverArtRequest coverRequest = new(request.UnityData, request.MenuArt);
            background = UnityCoverArtGenerator.TryLoadBackground(coverRequest) ?? CreateFallbackBackground();

            BundleContext internalRequest = new(
                request.SongName,
                coachImages,
                background,
                request.TemplatePath,
                request.OutputFolderPath,
                request.ForCustomServer);

            GenerateBundle(internalRequest);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
            background?.Dispose();
        }
    }

    private static void GenerateBundle(BundleContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBundleRequest(request);

        Logger.Log($"Converting CoachesLarge bundle for {request.Codename}...");
        try
        {
            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, List<AssetFileInfo>? sortedAssetInfos, AssetTypeValueField? assetBundleBase) = InitializeBundle(request);

            (AssetFileInfo? coachTextureTpl, AssetFileInfo? coachSpriteTpl, AssetFileInfo? backgroundTextureTpl, AssetFileInfo? backgroundSpriteTpl, long[]? textureIds, long[]? spriteIds) =
                ClearBundleAndIdentifyTemplates(request, manager, afileInst, afile, assetBundleBase);

            ProcessCoachAssets(request, manager, afileInst, afile, coachTextureTpl, coachSpriteTpl, textureIds, spriteIds);
            ProcessBackgroundAsset(request, manager, afileInst, backgroundTextureTpl, backgroundSpriteTpl);

            PopulatePreloadTable(assetBundleBase["m_PreloadTable"]["Array"], textureIds, spriteIds);
            PopulateAssetContainer(request, assetBundleBase["m_Container"]["Array"], textureIds, spriteIds);

            AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
            FinalizeAndSaveBundle(request, bunInst.file, afile, assetBundleBase, assetBundleInfo.SetNewData);
            Logger.Log($"Finished CoachesLarge bundle for {request.Codename}");
        }
        catch
        {
            Logger.Log($"Failed to generate CoachesLarge bundle for {request.Codename}", LogLevel.Error);
            throw;
        }
    }

    private static void ValidateInput(UnityCoachesLargeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.MenuArt);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
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

            string file = coachFiles[i];
            destination.Add(Image.Load<Rgba32>(file));
        }
    }

    private static Image<Rgba32> CreateFallbackBackground() => new(2048, 1024, Color.Magenta);

    private static void ValidateBundleRequest(BundleContext request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (request.CoachImages == null || request.CoachImages.Count == 0)
            throw new ArgumentException("At least one coach image is required.", nameof(request));
        if (request.BackgroundImage == null)
            throw new ArgumentException("Background image must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath) || !File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, List<AssetFileInfo> SortedAssetInfos, AssetTypeValueField AssetBundleBase)
        InitializeBundle(BundleContext request)
    {
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(request.TemplatePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{request.Codename}_CoachesLarge";
        assetBundleBase["m_AssetBundleName"].AsString = $"{request.Codename}_CoachesLarge";

        return (manager, bunInst, afileInst, afile, sortedAssetInfos, assetBundleBase);
    }

    private static (AssetFileInfo coachTexture, AssetFileInfo coachSprite, AssetFileInfo backgroundTexture, AssetFileInfo backgroundSprite, long[] textureIds, long[] spriteIds)
        ClearBundleAndIdentifyTemplates(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleBase)
    {
        AssetFileInfo? coachTexture = null;
        AssetFileInfo? coachSprite = null;
        AssetFileInfo? backgroundTexture = null;
        AssetFileInfo? backgroundSprite = null;

        int coachCount = request.CoachImages.Count;
        long[] textureIds = new long[coachCount + 1];
        long[] spriteIds = new long[coachCount + 1];

        AssetTypeValueField preloadArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField containerArray = assetBundleBase["m_Container"]["Array"];
        preloadArray.Children.Clear();
        containerArray.Children.Clear();

        List<AssetFileInfo> assetsToRemove = [];
        foreach (AssetFileInfo assetInfo in afile.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg", StringComparison.Ordinal))
            {
                backgroundTexture = assetInfo;
                textureIds[0] = assetInfo.PathId;
            }
            else if (assetBase["m_Name"].AsString.EndsWith("_Coach_1", StringComparison.Ordinal))
            {
                coachTexture = assetInfo;
                textureIds[1] = assetInfo.PathId;
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
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg", StringComparison.Ordinal))
            {
                backgroundSprite = assetInfo;
                spriteIds[0] = assetInfo.PathId;
            }
            else if (assetBase["m_Name"].AsString.EndsWith("_Coach_1", StringComparison.Ordinal))
            {
                coachSprite = assetInfo;
                spriteIds[1] = assetInfo.PathId;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetFileInfo assetInfo in assetsToRemove)
            afile.AssetInfos.Remove(assetInfo);

        if (coachTexture == null || coachSprite == null || backgroundTexture == null || backgroundSprite == null)
            throw new InvalidOperationException("Failed to locate required template assets for CoachesLarge bundle.");

        return (coachTexture, coachSprite, backgroundTexture, backgroundSprite, textureIds, spriteIds);
    }

    private static void ProcessCoachAssets(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile,
        AssetFileInfo coachTextureTpl, AssetFileInfo coachSpriteTpl, long[] textureIds, long[] spriteIds)
    {
        TextureFormat fmt = TextureFormat.DXT5Crunched;
        int mips = 1;

        for (int i = 1; i <= request.CoachImages.Count; i++)
        {
            long coachTextureId = i == 1 ? textureIds[1] : afile.GetRandomId();
            long coachSpriteId = i == 1 ? spriteIds[1] : afile.GetRandomId();

            AssetTypeValueField coachTextureBase = manager.GetBaseField(afileInst, coachTextureTpl);
            AssetTypeValueField coachSpriteBase = manager.GetBaseField(afileInst, coachSpriteTpl);

            coachTextureBase["m_Name"].AsString = $"{request.Codename}_Coach_{i}";
            coachSpriteBase["m_Name"].AsString = $"{request.Codename}_Coach_{i}";

            using Image<Rgba32> image = request.CoachImages[i - 1].CloneAs<Rgba32>();
            image.Mutate(x => x.Resize(1024, 1024));

            byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new InvalidOperationException("Failed to encode coach image.");

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

            textureIds[i] = coachTextureId;
            spriteIds[i] = coachSpriteId;
        }
    }

    private static void ProcessBackgroundAsset(BundleContext request, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo backgroundTexture, AssetFileInfo backgroundSprite)
    {
        AssetTypeValueField backgroundTextureBase = manager.GetBaseField(afileInst, backgroundTexture);
        AssetTypeValueField backgroundSpriteBase = manager.GetBaseField(afileInst, backgroundSprite);

        backgroundTextureBase["m_Name"].AsString = $"{request.Codename}_map_bkg";
        backgroundSpriteBase["m_Name"].AsString = $"{request.Codename}_map_bkg";

        using Image<Rgba32> image = request.BackgroundImage.CloneAs<Rgba32>();
        TextureFormat fmt = TextureFormat.DXT1Crunched;
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new InvalidOperationException("Failed to encode background image.");

        backgroundTextureBase["image data"].AsByteArray = encImageBytes;
        backgroundTextureBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        backgroundTextureBase["m_StreamData"]["offset"].AsULong = 0;
        backgroundTextureBase["m_StreamData"]["size"].AsUInt = 0;
        backgroundTextureBase["m_StreamData"]["path"].AsString = string.Empty;

        backgroundTexture.SetNewData(backgroundTextureBase);
        backgroundSprite.SetNewData(backgroundSpriteBase);
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
        int coachCount = request.CoachImages.Count;
        for (int i = 0; i < coachCount + 1; i++)
        {
            string name = i == 0 ? "CoachesBackground" : $"Coach{i}";

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

    private static void FinalizeAndSaveBundle(BundleContext request, AssetBundleFile bundle, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bundle.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bundle.SaveAndCompress(request.OutputFolderPath, request.ForCustomServer);
    }

    private sealed record BundleContext(
        string Codename,
        IReadOnlyList<Image<Rgba32>> CoachImages,
        Image<Rgba32> BackgroundImage,
        string TemplatePath,
        string OutputFolderPath,
        bool ForCustomServer);
}
