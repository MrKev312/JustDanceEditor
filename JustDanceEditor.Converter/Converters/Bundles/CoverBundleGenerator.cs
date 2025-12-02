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

    /// <summary>
    /// Generates a cover bundle from an image path, bypassing the full conversion context.
    /// </summary>
    /// <param name="codename">The codename of the song.</param>
    /// <param name="coverImage">The cover image to use.</param>
    /// <param name="templatePath">The path to the template cover bundle file.</param>
    /// <param name="outputFolderPath">The folder where the generated bundle will be saved.</param>
    /// <param name="forCustomServer">Whether to format the bundle for a custom server.</param>
    public static void GenerateCover(string codename, Image<Rgba32> coverImage, string templatePath, string outputFolderPath, bool forCustomServer)
    {
        try
        {
            Logger.Log($"Starting generation for cover: {codename}");

            // Resize the image to 640x360
            coverImage.Mutate(x => x.Resize(640, 360));

            // Initialize AssetsManager and load bundle data
            var (Manager, BunInst, AFileInst, AFile, AssetBundleInfo, AssetBundleBase, CoverTextureInfo, CoverSpriteInfo) =
                InitializeBundle(templatePath, codename);

            // Update the cover texture asset with the new image data
            UpdateCoverTexture(codename, Manager, AFileInst, CoverTextureInfo, coverImage);

            // Update the cover sprite asset
            UpdateCoverSprite(codename, Manager, AFileInst, CoverSpriteInfo);

            // Apply all changes to the AssetBundle and save the modified bundle file
            FinalizeAndSaveBundle(outputFolderPath, forCustomServer, BunInst.file, AFile, AssetBundleBase, AssetBundleInfo.SetNewData);

            Logger.Log($"Finished generating cover for {codename}");
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate cover for {codename}: {e.Message}", LogLevel.Error);
            throw;
        }
    }

    private static void GenerateCoverInternal(ConversionContext context)
    {
        Logger.Log("Converting Cover...");
        UnityExportData song = context.RequireUnityData();

        // Prepare the cover image from various sources
        using Image<Rgba32>? coverImage = PrepareCoverImage(context);
        if (coverImage == null)
        {
            Logger.Log("Cover image could not be loaded or generated, skipping cover bundle generation.", LogLevel.Warning);
            return;
        }

        // Generate the cover bundle using the prepared image
        GenerateCover(
            song.Name,
            coverImage,
            context.FileSystem.TemplateFiles.Cover,
            context.FileSystem.OutputFolders.CoverFolder,
            context.Request.ExportType == ExportType.CustomServer
        );
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetFileInfo AssetBundleInfo, AssetTypeValueField AssetBundleBase, AssetFileInfo CoverTextureInfo, AssetFileInfo CoverSpriteInfo)
        InitializeBundle(string templatePath, string codename)
    {
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(templatePath, true);
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundleInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.AssetBundle);
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundleInfo);

        assetBundleBase["m_Name"].AsString = $"{codename}_Cover";
        assetBundleBase["m_AssetBundleName"].AsString = $"{codename}_Cover";

        AssetFileInfo coverTextureInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Texture2D);
        AssetFileInfo coverSpriteInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Sprite);

        return (manager, bunInst, afileInst, afile, assetBundleInfo, assetBundleBase, coverTextureInfo, coverSpriteInfo);
    }

    private static Image<Rgba32>? PrepareCoverImage(ConversionContext context)
    {
        UnityExportData song = context.RequireUnityData();
        Image<Rgba32>? coverImage = null;
        if (context.Request.OnlineCover)
            coverImage ??= CoverArtGenerator.TryImageWeb(song.Name, "Cover");
        coverImage ??= CoverArtGenerator.ExistingCover(context);
        coverImage ??= CoverArtGenerator.GenerateOwnCover(context);

        if (coverImage == null)
        {
            Logger.Log("No cover image could be prepared.", LogLevel.Warning);
            return null;
        }

        // Save the image in the temp folder
        string tempCoverPath = Path.Combine(context.FileSystem.TempFolders.MenuArtFolder, $"Cover_{song.Name}.png");
        coverImage.Mutate(x => x.Resize(640, 360));
        coverImage.Save(tempCoverPath);
        Logger.Log($"Cover image prepared and saved to: {tempCoverPath}", LogLevel.Debug);

        // Resize the image to 640x360

        return coverImage;
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
        coverBase["m_StreamData"]["offset"].AsInt = 0; // Useless, but set it anyway
        coverBase["m_StreamData"]["size"].AsInt = 0;   // Useless, but set it anyway
        coverBase["m_StreamData"]["path"].AsString = "";

        coverInfo.SetNewData(coverBase);
    }

    private static void UpdateCoverSprite(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverSpriteInfo)
    {
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);
        coverSpriteBase["m_Name"].AsString = $"{codename}_Cover_2x";
        // Potentially other sprite properties might need updates if they depend on texture size or content,
        // but usually for covers, only the name and the texture link (implicit via AssetBundle structure) change.
        coverSpriteInfo.SetNewData(coverSpriteBase);
    }

    private static void FinalizeAndSaveBundle(string outputFolderPath, bool forCustomServer, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bun.SaveAndCompress(outputFolderPath, forCustomServer);
    }
}