using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoverBundleGenerator
{
    public async static Task GenerateCoverAsync(ConversionContext context) =>
        await Task.Run(() => GenerateCover(context));

    public static void GenerateCover(ConversionContext context)
    {
        try
        {
            GenerateCoverInternal(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate cover: {e.Message}", LogLevel.Error);
        }

        Logger.Log("Finished generating cover");
    }

    private static void GenerateCoverInternal(ConversionContext context)
    {
        Logger.Log("Converting Cover...");

        // Initialize AssetsManager and load bundle data
        var (Manager, BunInst, AFileInst, AFile, AssetBundleInfo, AssetBundleBase, CoverTextureInfo, CoverSpriteInfo) = InitializeBundle(context);

        // Prepare the cover image from various sources
        using Image<Rgba32>? coverImage = PrepareCoverImage(context);
        if (coverImage == null)
        {
            Logger.Log("Cover image could not be loaded or generated, skipping cover bundle generation.", LogLevel.Warning);
            return;
        }

        // Update the cover texture asset with the new image data
        UpdateCoverTexture(context, Manager, AFileInst, CoverTextureInfo, coverImage);

        // Update the cover sprite asset
        UpdateCoverSprite(context, Manager, AFileInst, CoverSpriteInfo);

        // Apply all changes to the AssetBundle and save the modified bundle file
        FinalizeAndSaveBundle(context, BunInst.file, AFile, AssetBundleBase, AssetBundleInfo.SetNewData);
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetFileInfo AssetBundleInfo, AssetTypeValueField AssetBundleBase, AssetFileInfo CoverTextureInfo, AssetFileInfo CoverSpriteInfo)
        InitializeBundle(ConversionContext context)
    {
        string coverPackagePath = context.FileSystem.TemplateFiles.Cover;
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coverPackagePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{context.SongData.Name}_Cover";
        assetBundleBase["m_AssetBundleName"].AsString = $"{context.SongData.Name}_Cover";
        // AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"]; // Not directly modified here but good to be aware of

        AssetFileInfo coverTextureInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Texture2D);
        AssetFileInfo coverSpriteInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Sprite);

        return (manager, bunInst, afileInst, afile, assetBundleInfo, assetBundleBase, coverTextureInfo, coverSpriteInfo);
    }

    private static Image<Rgba32>? PrepareCoverImage(ConversionContext context)
    {
        Image<Rgba32>? coverImage = null;
        if (context.Request.OnlineCover)
            coverImage ??= CoverArtGenerator.TryImageWeb(context, "Cover");
        coverImage ??= CoverArtGenerator.ExistingCover(context);
        coverImage ??= CoverArtGenerator.GenerateOwnCover(context);

        if (coverImage != null)
        {
            // Save the image in the temp folder
            string tempCoverPath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"Cover_{context.SongData.Name}.png");
            coverImage.Save(tempCoverPath);
            Logger.Log($"Cover image prepared and saved to: {tempCoverPath}", LogLevel.Debug);
        }

        return coverImage;
    }

    private static void UpdateCoverTexture(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverInfo, Image<Rgba32> coverImage)
    {
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);
        coverBase["m_Name"].AsString = $"{context.SongData.Name}_Cover_2x";

        TextureFormat fmt = TextureFormat.DXT1Crunched;
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(coverImage, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode cover image!");

        coverBase["image data"].AsByteArray = encImageBytes;
        coverBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        coverBase["m_StreamData"]["offset"].AsInt = 0; // Useless, but set it anyway
        coverBase["m_StreamData"]["size"].AsInt = 0;   // Useless, but set it anyway
        coverBase["m_StreamData"]["path"].AsString = "";

        coverInfo.SetNewData(coverBase);
    }

    private static void UpdateCoverSprite(ConversionContext context, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverSpriteInfo)
    {
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);
        coverSpriteBase["m_Name"].AsString = $"{context.SongData.Name}_Cover_2x";
        // Potentially other sprite properties might need updates if they depend on texture size or content,
        // but usually for covers, only the name and the texture link (implicit via AssetBundle structure) change.
        coverSpriteInfo.SetNewData(coverSpriteBase);
    }

    private static void FinalizeAndSaveBundle(ConversionContext context, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        string outputPackagePath = context.FileSystem.OutputFolders.CoverFolder;
        bun.SaveAndCompress(outputPackagePath, context.Request.ExportType == ExportType.CustomServer);
    }
}