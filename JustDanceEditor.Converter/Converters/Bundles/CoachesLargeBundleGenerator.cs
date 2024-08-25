using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Converters.Images;
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
    public async static Task GenerateCoachesLargeAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => GenerateCoachesLarge(convert));
    public static void GenerateCoachesLarge(ConvertUbiArtToUnity convert)
    {
        try
        {
            GenerateCoachesLargeInteral(convert);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate CoachesLarge: {e.Message}", LogLevel.Error);
        }
    }

    static void GenerateCoachesLargeInteral(ConvertUbiArtToUnity convert)
    {
        // Get the coaches folder
        // /template/cachex/CoachesLarge/*
        string coacheLargePackagePath = Directory.GetFiles(Path.Combine(convert.TemplateFolder, "CoachesLarge"))[0];

        Logger.Log("Converting CoachesLarge...");
        // Open the coaches package using AssetTools.NET
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coacheLargePackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{convert.SongData.Name}_CoachesLarge";
        assetBundleBase["m_AssetBundleName"].AsString = $"{convert.SongData.Name}_CoachesLarge";
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField assetBundleContainer = assetBundleBase["m_Container"]["Array"];

        AssetFileInfo? coachTexture = null;
        AssetFileInfo? coachSprite = null;
        AssetFileInfo? bkgTexture = null;
        AssetFileInfo? bkgSprite = null;

        long[] TextureIDs = new long[convert.SongData.CoachCount + 1];
        long[] SpriteIDs = new long[convert.SongData.CoachCount + 1];

        // First we clean out the bundle
        // Clearing the preload table and the container
        assetBundleArray.Children.Clear();
        assetBundleContainer.Children.Clear();

        // Removing all the textures
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);

            // If assetBase["m_Name"].AsString ends with _map_bkg, it's the background texture
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg"))
            {
                bkgTexture = assetInfo;
                TextureIDs[0] = assetInfo.PathId;
                continue;
            }

            // If assetBase["m_Name"].AsString ends with _Coach_1, it's the coach texture
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1"))
            {
                coachTexture = assetInfo;
                TextureIDs[1] = assetInfo.PathId;
                continue;
            }

            // Then remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);
        }

        // Also remove their corresponding Sprites
        foreach (AssetFileInfo assetInfo in sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite))
        {
            AssetTypeValueField assetBase = manager.GetBaseField(afileInst, assetInfo);

            // If assetBase["m_Name"].AsString ends with _map_bkg, it's the background sprite
            if (assetBase["m_Name"].AsString.EndsWith("_map_bkg"))
            {
                bkgSprite = assetInfo;
                SpriteIDs[0] = assetInfo.PathId;
                continue;
            }

            // If assetBase["m_Name"].AsString ends with _Coach_1, it's the coach sprite
            if (assetBase["m_Name"].AsString.EndsWith("_Coach_1"))
            {
                coachSprite = assetInfo;
                SpriteIDs[1] = assetInfo.PathId;
                continue;
            }

            // Else, remove it from the bundle
            afile.AssetInfos.Remove(assetInfo);
        }

        if (coachTexture == null || coachSprite == null || bkgTexture == null || bkgSprite == null)
        {
            throw new Exception("Failed to find the required textures and sprites!");
        }

        // For each coach, we add the texture and the sprite
        byte[] encImageBytes;
        TextureFormat fmt = TextureFormat.DXT5Crunched;
        int mips = 1;

        for (int i = 1; i <= convert.SongData.CoachCount; i++)
        {
            long coachTextureID = i == 0 ?
                TextureIDs[1] :
                afile.GetRandomId();
            long coachSpriteID = i == 0 ?
                SpriteIDs[1] :
                afile.GetRandomId();

            AssetTypeValueField coachTextureBaseField = manager.GetBaseField(afileInst, coachTexture);
            AssetTypeValueField coachSpriteBaseField = manager.GetBaseField(afileInst, coachSprite);

            // Create the new texture
            coachTextureBaseField["m_Name"].AsString = $"{convert.SongData.Name}_Coach_{i}";
            coachSpriteBaseField["m_Name"].AsString = $"{convert.SongData.Name}_Coach_{i}";

            string path = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_Coach_{i}.tga.png");

            // Load the image
            Image<Rgba32> image = Image.Load<Rgba32>(path);
            image.Mutate(x => x.Resize(1024, 1024));

            encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips) ?? throw new Exception("Failed to encode image!");

            // Set the image data
            coachTextureBaseField["image data"].AsByteArray = encImageBytes;
            coachTextureBaseField["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coachTextureBaseField["m_StreamData"]["offset"].AsULong = 0;
            coachTextureBaseField["m_StreamData"]["size"].AsUInt = 0;
            coachTextureBaseField["m_StreamData"]["path"].AsString = "";

            if (i == 1)
            {
                coachTexture.SetNewData(coachTextureBaseField);
                coachSprite.SetNewData(coachSpriteBaseField);
                continue;
            }

            uint[] uintArray = Guid.NewGuid().ToUnity();

            // Use the GUID for the texture as the key
            coachSpriteBaseField["m_RenderDataKey"]["first"]["data[0]"].AsUInt = uintArray[0];
            coachSpriteBaseField["m_RenderDataKey"]["first"]["data[1]"].AsUInt = uintArray[1];
            coachSpriteBaseField["m_RenderDataKey"]["first"]["data[2]"].AsUInt = uintArray[2];
            coachSpriteBaseField["m_RenderDataKey"]["first"]["data[3]"].AsUInt = uintArray[3];

            // Set the texture ID to point to the new texture
            coachSpriteBaseField["m_RD"]["texture"]["m_PathID"].AsLong = coachTextureID;

            // Make a new AssetFileInfo
            AssetFileInfo newTextureInfo = AssetFileInfo.Create(afile, coachTextureID, (int)AssetClassID.Texture2D, null);
            AssetFileInfo newSpriteInfo = AssetFileInfo.Create(afile, coachSpriteID, (int)AssetClassID.Sprite, null);
            newTextureInfo.SetNewData(coachTextureBaseField);
            newSpriteInfo.SetNewData(coachSpriteBaseField);

            // Add the new AssetFileInfo to the AssetFile
            afile.Metadata.AddAssetInfo(newTextureInfo);
            afile.Metadata.AddAssetInfo(newSpriteInfo);
            TextureIDs[i] = coachTextureID;
            SpriteIDs[i] = coachSpriteID;
        }

        // Now let's do the background
        {
            AssetTypeValueField bkgTextureBaseField = manager.GetBaseField(afileInst, bkgTexture);
            AssetTypeValueField bkgSpriteBaseField = manager.GetBaseField(afileInst, bkgSprite);

            // Create the new texture
            bkgTextureBaseField["m_Name"].AsString = $"{convert.SongData.Name}_map_bkg";
            bkgSpriteBaseField["m_Name"].AsString = $"{convert.SongData.Name}_map_bkg";

            // Load the image
            Image<Rgba32> image = CoverArtGenerator.GetBackground(convert);

            fmt = TextureFormat.DXT1Crunched;

            encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips) ?? throw new Exception("Failed to encode image!");

            // Set the image data
            bkgTextureBaseField["image data"].AsByteArray = encImageBytes;
            bkgTextureBaseField["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            bkgTextureBaseField["m_StreamData"]["offset"].AsULong = 0;
            bkgTextureBaseField["m_StreamData"]["size"].AsUInt = 0;
            bkgTextureBaseField["m_StreamData"]["path"].AsString = "";

            bkgTexture.SetNewData(bkgTextureBaseField);
            bkgSprite.SetNewData(bkgSpriteBaseField);
        }

        // Let's add everything to the preload table
        foreach (long id in TextureIDs.Union(SpriteIDs))
        {
            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleArray);
            newAssetBundle["m_PathID"].AsLong = id;
            assetBundleArray.Children.Add(newAssetBundle);
        }

        // Finally we fix the container
        for (int i = 0; i < convert.SongData.CoachCount + 1; i++)
        {
            string name = i == 0 ? "CoachesBackground" : $"Coach{i}";

            AssetTypeValueField newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            newAssetBundle["first"].AsString = name;
            newAssetBundle["second"]["preloadIndex"].AsInt = 2 * i;
            newAssetBundle["second"]["preloadSize"].AsInt = 2;
            newAssetBundle["second"]["asset"]["m_PathID"].AsLong = TextureIDs[i];
            assetBundleContainer.Children.Add(newAssetBundle);

            newAssetBundle = ValueBuilder.DefaultValueFieldFromArrayTemplate(assetBundleContainer);
            newAssetBundle["first"].AsString = name;
            newAssetBundle["second"]["preloadIndex"].AsInt = 2 * i;
            newAssetBundle["second"]["preloadSize"].AsInt = 2;
            newAssetBundle["second"]["asset"]["m_PathID"].AsLong = SpriteIDs[i];
            assetBundleContainer.Children.Add(newAssetBundle);
        }

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        string outputPackagePath = Path.Combine(convert.OutputXFolder, "CoachesLarge");
        bun.SaveAndCompress(outputPackagePath);
    }
}
