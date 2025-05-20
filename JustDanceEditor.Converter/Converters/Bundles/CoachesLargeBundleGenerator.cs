using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoachesLargeBundleGenerator
{
    public async static Task GenerateCoachesLargeAsync(ConversionContext context) =>
        await Task.Run(() => GenerateCoachesLarge(context));

    public static void GenerateCoachesLarge(ConversionContext context)
    {
        try
        {
            GenerateCoachesLargeInternal(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate CoachesLarge: {e.Message}", LogLevel.Error);
        }

        Logger.Log("Finished generating CoachesLarge");
    }

    private static void GenerateCoachesLargeInternal(ConversionContext context)
    {
        Logger.Log("Converting CoachesLarge...");

        // Initialize AssetsManager and load bundle data
        var (Manager, BunInst, AFileInst, AFile, SortedAssetInfos, AssetBundleBase) = InitializeBundle(context);

        // Clear existing assets and identify template assets for coaches and background
        var (coachTextureTpl, coachSpriteTpl, bkgTextureTpl, bkgSpriteTpl, textureIDs, spriteIDs) =
            ClearBundleAndIdentifyTemplates(context, Manager, AFileInst, AFile, AssetBundleBase);

        // Process and add textures and sprites for each coach
        ProcessCoachAssets(context, Manager, AFileInst, AFile, coachTextureTpl, coachSpriteTpl, textureIDs, spriteIDs);

        // Process and update the background texture and sprite
        ProcessBackgroundAssets(context, Manager, AFileInst, bkgTextureTpl, bkgSpriteTpl);

        // Populate the AssetBundle's preload table with all new and updated assets
        PopulatePreloadTable(AssetBundleBase["m_PreloadTable"]["Array"], textureIDs, spriteIDs);

        // Populate the AssetBundle's container with references to coach and background assets
        PopulateAssetContainer(context, AssetBundleBase["m_Container"]["Array"], textureIDs, spriteIDs);

        // Apply all changes to the AssetBundle and save the modified bundle file
        FinalizeAndSaveBundle(context, BunInst.file, AFile, AssetBundleBase, assetBundleData => SortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle).SetNewData(assetBundleData));
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, List<AssetFileInfo> SortedAssetInfos, AssetTypeValueField AssetBundleBase) InitializeBundle(ConversionContext context)
    {
        string coacheLargePackagePath = context.FileSystem.TemplateFiles.CoachesLarge;
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coacheLargePackagePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{context.SongData.Name}_CoachesLarge";
        assetBundleBase["m_AssetBundleName"].AsString = $"{context.SongData.Name}_CoachesLarge";

        return (manager, bunInst, afileInst, afile, sortedAssetInfos, assetBundleBase);
    }

    private static (AssetFileInfo coachTexture, AssetFileInfo coachSprite, AssetFileInfo bkgTexture, AssetFileInfo bkgSprite, long[] textureIDs, long[] spriteIDs)
        ClearBundleAndIdentifyTemplates(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleBase)
    {
        AssetFileInfo? coachTexture = null;
        AssetFileInfo? coachSprite = null;
        AssetFileInfo? bkgTexture = null;
        AssetFileInfo? bkgSprite = null;

        long[] textureIDs = new long[context.SongData.CoachCount + 1];
        long[] spriteIDs = new long[context.SongData.CoachCount + 1];

        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField assetBundleContainer = assetBundleBase["m_Container"]["Array"];
        assetBundleArray.Children.Clear();
        assetBundleContainer.Children.Clear();

        List<AssetFileInfo> assetsToRemove = [];
        foreach (AssetFileInfo assetInfo in afile.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg"))
            {
                bkgTexture = assetInfo;
                textureIDs[0] = assetInfo.PathId;
            }
            else if (assetBase["m_Name"].AsString.EndsWith("_Coach_1"))
            {
                coachTexture = assetInfo;
                textureIDs[1] = assetInfo.PathId;
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
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg"))
            {
                bkgSprite = assetInfo;
                spriteIDs[0] = assetInfo.PathId;
            }
            else if (assetBase["m_Name"].AsString.EndsWith("_Coach_1"))
            {
                coachSprite = assetInfo;
                spriteIDs[1] = assetInfo.PathId;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetFileInfo assetInfo in assetsToRemove)
            afile.AssetInfos.Remove(assetInfo);

        if (coachTexture == null || coachSprite == null || bkgTexture == null || bkgSprite == null)
            throw new Exception("Failed to find the required template textures and sprites!");

        return (coachTexture, coachSprite, bkgTexture, bkgSprite, textureIDs, spriteIDs);
    }

    private static void ProcessCoachAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile,
        AssetFileInfo coachTextureTpl, AssetFileInfo coachSpriteTpl, long[] textureIDs, long[] spriteIDs)
    {
        TextureFormat fmt = TextureFormat.DXT5Crunched;
        int mips = 1;

        for (int i = 1; i <= context.SongData.CoachCount; i++)
        {
            long coachTextureID = i == 1 ? textureIDs[1] : afile.GetRandomId();
            long coachSpriteID = i == 1 ? spriteIDs[1] : afile.GetRandomId();

            AssetTypeValueField coachTextureBaseField = manager.GetBaseField(afileInst, coachTextureTpl);
            AssetTypeValueField coachSpriteBaseField = manager.GetBaseField(afileInst, coachSpriteTpl);

            coachTextureBaseField["m_Name"].AsString = $"{context.SongData.Name}_Coach_{i}";
            coachSpriteBaseField["m_Name"].AsString = $"{context.SongData.Name}_Coach_{i}";

            string path = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_Coach_{i}.png");
            using Image<Rgba32> image = Image.Load<Rgba32>(path);
            image.Mutate(x => x.Resize(1024, 1024));

            byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode coach image!");

            coachTextureBaseField["image data"].AsByteArray = encImageBytes;
            coachTextureBaseField["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coachTextureBaseField["m_StreamData"]["offset"].AsULong = 0;
            coachTextureBaseField["m_StreamData"]["size"].AsUInt = 0;
            coachTextureBaseField["m_StreamData"]["path"].AsString = "";

            if (i == 1)
            {
                coachTextureTpl.SetNewData(coachTextureBaseField);
                coachSpriteTpl.SetNewData(coachSpriteBaseField);
            }
            else
            {
                uint[] uintArray = Guid.NewGuid().ToUnity();
                coachSpriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = uintArray[0];
                coachSpriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = uintArray[1];
                coachSpriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = uintArray[2];
                coachSpriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = uintArray[3];
                coachSpriteBaseField["m_RD"]["texture"]["m_PathID"].AsLong = coachTextureID;

                AssetFileInfo newTextureInfo = AssetFileInfo.Create(afile, coachTextureID, (int)AssetClassID.Texture2D, null);
                AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, coachSpriteID, (int)AssetClassID.Sprite, null);
                newTextureInfo.SetNewData(coachTextureBaseField);
                newSpriteInfo.SetNewData(coachSpriteBaseField);

                afile.Metadata.AddAssetInfo(newTextureInfo);
                afile.Metadata.AddAssetInfo(newSpriteInfo);
            }

            textureIDs[i] = coachTextureID;
            spriteIDs[i] = coachSpriteID;
        }
    }

    private static void ProcessBackgroundAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo bkgTexture, AssetFileInfo bkgSprite)
    {
        AssetTypeValueField bkgTextureBaseField = manager.GetBaseField(afileInst, bkgTexture);
        AssetTypeValueField bkgSpriteBaseField = manager.GetBaseField(afileInst, bkgSprite);

        bkgTextureBaseField["m_Name"].AsString = $"{context.SongData.Name}_map_bkg";
        bkgSpriteBaseField["m_Name"].AsString = $"{context.SongData.Name}_map_bkg";

        using Image<Rgba32> image = CoverArtGenerator.GetBackground(context);
        TextureFormat fmt = TextureFormat.DXT1Crunched;
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode background image!");

        bkgTextureBaseField["image data"].AsByteArray = encImageBytes;
        bkgTextureBaseField["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        bkgTextureBaseField["m_StreamData"]["offset"].AsULong = 0;
        bkgTextureBaseField["m_StreamData"]["size"].AsUInt = 0;
        bkgTextureBaseField["m_StreamData"]["path"].AsString = "";

        bkgTexture.SetNewData(bkgTextureBaseField);
        bkgSprite.SetNewData(bkgSpriteBaseField);
    }

    private static void PopulatePreloadTable(AssetTypeValueField assetBundleArray, long[] textureIDs, long[] spriteIDs)
    {
        foreach (long id in textureIDs.Union(spriteIDs))
        {
            AssetTypeValueField newPreloadEntry = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newPreloadEntry["m_PathID"].AsLong = id;
            assetBundleArray.Children.Add(newPreloadEntry);
        }
    }

    private static void PopulateAssetContainer(ConversionContext context, AssetTypeValueField assetBundleContainer, long[] textureIDs, long[] spriteIDs)
    {
        for (int i = 0; i < context.SongData.CoachCount + 1; i++)
        {
            string name = i == 0 ? "CoachesBackground" : $"Coach{i}";

            AssetTypeValueField newContainerEntryTexture = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            newContainerEntryTexture["first"].AsString = name;
            newContainerEntryTexture["second"]["preloadIndex"].AsInt = 2 * i;
            newContainerEntryTexture["second"]["preloadSize"].AsInt = 2;
            newContainerEntryTexture["second"]["asset"]["m_PathID"].AsLong = textureIDs[i];
            assetBundleContainer.Children.Add(newContainerEntryTexture);

            AssetTypeValueField newContainerEntrySprite = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            newContainerEntrySprite["first"].AsString = name; // Name might need to be distinct if used as a key, e.g., name + "_Sprite"
            newContainerEntrySprite["second"]["preloadIndex"].AsInt = 2 * i; // PreloadIndex refers to the start of the related group in preloadTable
            newContainerEntrySprite["second"]["preloadSize"].AsInt = 2;    // PreloadSize is the count of assets in that group
            newContainerEntrySprite["second"]["asset"]["m_PathID"].AsLong = spriteIDs[i];
            assetBundleContainer.Children.Add(newContainerEntrySprite);
        }
    }

    private static void FinalizeAndSaveBundle(ConversionContext context, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        string outputPackagePath = context.FileSystem.OutputFolders.CoachesLargeFolder;
        bun.SaveAndCompress(outputPackagePath, context.Request.ExportType == ExportType.CustomServer);
    }
}