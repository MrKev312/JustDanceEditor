using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using TextureConverter.TextureConverterHelpers;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TextureConverter;
using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Core;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class SongTitleBundleGenerator
{
    public async static Task GenerateSongTitleLogoAsync(ConversionContext context) =>
        await Task.Run(() => GenerateSongTitleLogo(context));

    public static void GenerateSongTitleLogo(ConversionContext context)
    {
        try
        {
            GenerateSongTitleLogoInternal(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate song title logo: {e.Message}", LogLevel.Error);
        }

        Logger.Log("Finished generating song title logo");
    }

    /// <summary>
    /// Generates a song title logo bundle from a user-provided image path, bypassing the full conversion context.
    /// </summary>
    /// <param name="codename">The codename of the song.</param>
    /// <param name="imagePath">The path to the song title logo image.</param>
    /// <param name="templatePath">The path to the template song title logo bundle file.</param>
    /// <param name="outputFolderPath">The folder where the generated bundle will be saved.</param>
    /// <param name="forCustomServer">Whether to format the bundle for a custom server.</param>
    public static void GenerateSongTitleLogo(string codename, Image<Rgba32> titleImage, string templatePath, string outputFolderPath, bool forCustomServer)
    {
        try
        {
            Logger.Log($"Starting trivial generation for song title logo: {codename}");

            // Initialize AssetsManager and load bundle data
            var (Manager, BunInst, AFileInst, AFile, AssetBundleInfo, AssetBundleBase, TextureInfo, SpriteInfo) =
                InitializeBundle(templatePath, codename);

            // Process the image (resize, pad) and update the texture asset
            UpdateSongTitleTexture(codename, Manager, AFileInst, TextureInfo, titleImage);

            // Update the sprite asset associated with the song title
            UpdateSongTitleSprite(codename, Manager, AFileInst, SpriteInfo);

            // Apply all changes to the AssetBundle and save the modified bundle file
            FinalizeAndSaveBundle(outputFolderPath, forCustomServer, BunInst.file, AFile, AssetBundleBase, AssetBundleInfo.SetNewData);

            Logger.Log($"Finished generating trivial song title logo for {codename}");
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate trivial song title logo for {codename}: {e.Message}", LogLevel.Error);
            throw;
        }
    }

    private static void GenerateSongTitleLogoInternal(ConversionContext context)
    {
        // Attempt to load or find the song title image
        using Image<Rgba32>? titleImage = PrepareSongTitleImage(context);
        if (titleImage == null)
        {
            Logger.Log("No songTitleLogo.png found or generated, skipping song title logo bundle generation.", LogLevel.Important);
            return;
        }

        Logger.Log("Converting SongTitleLogo...");

        // Call the original GenerateSongTitleLogo function
        GenerateSongTitleLogo(
            context.SongData.Name,
            titleImage,
            context.FileSystem.TemplateFiles.SongTitleLogo,
            context.FileSystem.OutputFolders.SongTitleLogoFolder,
            context.Request.ExportType == ExportType.CustomServer
        );
    }

    private static Image<Rgba32>? PrepareSongTitleImage(ConversionContext context)
    {
        Image<Rgba32>? image = null;
        if (context.Request.OnlineCover)
            image = CoverArtGenerator.TryImageWeb(context, "Title");
        image ??= CoverArtGenerator.ExistingSongTitleLogo(context);

        // No generation step for song title if not found, unlike cover.
        return image;
    }

    private static (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetFileInfo AssetBundleInfo, AssetTypeValueField AssetBundleBase, AssetFileInfo TextureInfo, AssetFileInfo SpriteInfo)
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

        assetBundleBase["m_Name"].AsString = $"{codename}_SongTitleLogo";
        assetBundleBase["m_AssetBundleName"].AsString = $"{codename}_SongTitleLogo";

        AssetFileInfo textureInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Texture2D);
        AssetFileInfo spriteInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Sprite);

        return (manager, bunInst, afileInst, afile, assetBundleInfo, assetBundleBase, textureInfo, spriteInfo);
    }

    private static void UpdateSongTitleTexture(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo textureInfo, Image<Rgba32> image)
    {
        AssetTypeValueField textureBase = manager.GetBaseField(afileInst, textureInfo);
        textureBase["m_Name"].AsString = $"{codename}_Title";

        // Ensure 2:1 aspect ratio and 1024x512 size
        if (image.Width / (float)image.Height != 2f)
        {
            int newWidth = image.Height * 2;
            image.Mutate(x => x.Pad(newWidth, image.Height)); // Pad to 2:1
        }

        image.Mutate(x => x.Resize(1024, 512)); // Resize to target dimensions

        TextureFormat fmt = TextureFormat.DXT5Crunched; // DXT5 for alpha
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(image, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode song title image!");

        textureBase["image data"].AsByteArray = encImageBytes;
        textureBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        textureBase["m_StreamData"]["offset"].AsInt = 0;
        textureBase["m_StreamData"]["size"].AsInt = 0;
        textureBase["m_StreamData"]["path"].AsString = "";

        textureInfo.SetNewData(textureBase);
    }

    private static void UpdateSongTitleSprite(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo spriteInfo)
    {
        AssetTypeValueField spriteBase = manager.GetBaseField(afileInst, spriteInfo);
        spriteBase["m_Name"].AsString = $"{codename}_Title";
        // Sprite properties might need adjustment based on new texture dimensions or content (e.g., m_Rect, m_RD.textureRect).
        // Assuming template sprite settings are general enough or handled by implicit texture link.
        spriteInfo.SetNewData(spriteBase);
    }

    private static void FinalizeAndSaveBundle(string outputFolderPath, bool keepExtension, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bun.SaveAndCompress(outputFolderPath, keepExtension);
    }
}