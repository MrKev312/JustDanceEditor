using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoachesSmallBundleGenerator
{
    public async static Task GenerateCoachesSmallAsync(ConversionContext context) =>
        await Task.Run(() => GenerateCoachesSmall(context));

    public static void GenerateCoachesSmall(ConversionContext context)
    {
        try
        {
            GenerateCoachesSmallInternal(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate CoachesSmall: {e.Message}", LogLevel.Error);
        }

        Logger.Log("Finished generating CoachesSmall");
    }

    private static void GenerateCoachesSmallInternal(ConversionContext context)
    {
        Logger.Log("Converting CoachesSmall...");

        // Initialize AssetsManager and load bundle data
        var (Manager, BunInst, AFileInst, AFile, SortedAssetInfos, AssetBundleBase) = InitializeBundle(context);

        // Clear existing phone coach assets and identify template assets
        var (coachTextureTpl, coachSpriteTpl, textureIDs, spriteIDs) =
            ClearBundleAndIdentifyTemplates(context, Manager, AFileInst, AFile, AssetBundleBase);

        // Process and add textures and sprites for each phone coach
        ProcessCoachPhoneAssets(context, Manager, AFileInst, AFile, coachTextureTpl, coachSpriteTpl, textureIDs, spriteIDs);

        // Populate the AssetBundle's preload table with all new and updated phone coach assets
        PopulatePreloadTable(AssetBundleBase["m_PreloadTable"]["Array"], textureIDs, spriteIDs);

        // Populate the AssetBundle's container with references to phone coach assets
        PopulateAssetContainer(context, AssetBundleBase["m_Container"]["Array"], textureIDs, spriteIDs);

        // Apply all changes to the AssetBundle and save the modified bundle file
        FinalizeAndSaveBundle(context, BunInst.file, AFile, AssetBundleBase, assetBundleData => SortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle).SetNewData(assetBundleData));
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, List<AssetFileInfo> SortedAssetInfos, AssetTypeValueField AssetBundleBase) InitializeBundle(ConversionContext context)
    {
        string coacheSmallPackagePath = context.FileSystem.TemplateFiles.CoachesSmall;
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coacheSmallPackagePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{context.SongData.Name}_CoachesSmall";
        assetBundleBase["m_AssetBundleName"].AsString = $"{context.SongData.Name}_CoachesSmall";

        return (manager, bunInst, afileInst, afile, sortedAssetInfos, assetBundleBase);
    }

    private static (AssetFileInfo coachTexture, AssetFileInfo coachSprite, long[] textureIDs, long[] spriteIDs)
        ClearBundleAndIdentifyTemplates(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile, AssetTypeValueField assetBundleBase)
    {
        AssetFileInfo? coachTexture = null;
        AssetFileInfo? coachSprite = null;

        long[] textureIDs = new long[context.SongData.CoachCount];
        long[] spriteIDs = new long[context.SongData.CoachCount];

        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField assetBundleContainer = assetBundleBase["m_Container"]["Array"];
        assetBundleArray.Children.Clear();
        assetBundleContainer.Children.Clear();

        List<AssetFileInfo> assetsToRemove = [];
        foreach (AssetFileInfo assetInfo in afile.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1_Phone"))
            {
                coachTexture = assetInfo;
                textureIDs[0] = assetInfo.PathId;
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
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1_Phone"))
            {
                coachSprite = assetInfo;
                spriteIDs[0] = assetInfo.PathId;
            }
            else
            {
                assetsToRemove.Add(assetInfo);
            }
        }

        foreach (AssetFileInfo assetInfo in assetsToRemove)
            afile.AssetInfos.Remove(assetInfo);

        if (coachTexture == null || coachSprite == null)
            throw new Exception("Failed to find the required template phone textures and sprites!");

        return (coachTexture, coachSprite, textureIDs, spriteIDs);
    }

    private static void ProcessCoachPhoneAssets(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetsFile afile,
        AssetFileInfo coachTextureTpl, AssetFileInfo coachSpriteTpl, long[] textureIDs, long[] spriteIDs)
    {
        TextureFormat fmt = TextureFormat.DXT5Crunched;
        int mips = 1;

        for (int i = 1; i <= context.SongData.CoachCount; i++)
        {
            long coachTextureID = i == 1 ? textureIDs[0] : afile.GetRandomId();
            long coachSpriteID = i == 1 ? spriteIDs[0] : afile.GetRandomId();

            AssetTypeValueField coachTextureBaseField = manager.GetBaseField(afileInst, coachTextureTpl);
            AssetTypeValueField coachSpriteBaseField = manager.GetBaseField(afileInst, coachSpriteTpl);

            coachTextureBaseField["m_Name"].AsString = $"{context.SongData.Name}_Coach_{i}_Phone";
            coachSpriteBaseField["m_Name"].AsString = $"{context.SongData.Name}_Coach_{i}_Phone";

            string path = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"{context.SongData.Name}_Coach_{i}.png");
            using Image<Rgba32> image = Image.Load<Rgba32>(path);
            image.Mutate(x => x.Resize(256, 256));

            byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode coach phone image!");

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

            textureIDs[i - 1] = coachTextureID;
            spriteIDs[i - 1] = coachSpriteID;
        }
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
        for (int i = 0; i < context.SongData.CoachCount; i++)
        {
            string name = $"Coach{i + 1}_Phone";

            AssetTypeValueField newContainerEntryTexture = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            newContainerEntryTexture["first"].AsString = name;
            newContainerEntryTexture["second"]["preloadIndex"].AsInt = 2 * i;
            newContainerEntryTexture["second"]["preloadSize"].AsInt = 2;
            newContainerEntryTexture["second"]["asset"]["m_PathID"].AsLong = textureIDs[i];
            assetBundleContainer.Children.Add(newContainerEntryTexture);

            AssetTypeValueField newContainerEntrySprite = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            // Assuming the name for sprite is the same as texture for container key, or adjust if it needs to be distinct
            newContainerEntrySprite["first"].AsString = name;
            newContainerEntrySprite["second"]["preloadIndex"].AsInt = 2 * i;
            newContainerEntrySprite["second"]["preloadSize"].AsInt = 2;
            newContainerEntrySprite["second"]["asset"]["m_PathID"].AsLong = spriteIDs[i];
            assetBundleContainer.Children.Add(newContainerEntrySprite);
        }
    }

    private static void FinalizeAndSaveBundle(ConversionContext context, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        string outputPackagePath = context.FileSystem.OutputFolders.CoachesSmallFolder;
        bun.SaveAndCompress(outputPackagePath, context.Request.ExportType == ExportType.CustomServer);
    }
}