using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using TextureConverter.TextureConverterHelpers;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TextureConverter;
using JustDanceEditor.Converter.Files;
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

    static void GenerateSongTitleLogoInternal(ConversionContext context)
    {
        FileSystem fs = context.FileSystem;

        Image<Rgba32>? image = null;
        // Should we look up the cover online?
        if (context.Request.OnlineCover)
            image = CoverArtGenerator.TryImageWeb(context, "Title");

        // If we couldn't find the cover online, try to load it from the input folder
        image ??= CoverArtGenerator.ExistingSongTitleLogo(context);

        // If we still don't have a cover, we throw an info message
        if (image == null)
        {
            Logger.Log("No songTitleLogo.png found, skipping...", LogLevel.Important);
            return;
        }

        string songTitleLogoPackagePath = fs.TemplateFiles.SongTitleLogo;

        Logger.Log("Converting SongTitleLogo...");

        // Open the coaches package using AssetTools.NET
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(songTitleLogoPackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{context.SongData.Name}_SongTitleLogo";
        assetBundleBase["m_AssetBundleName"].AsString = $"{context.SongData.Name}_SongTitleLogo";
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // There's only one texture2d in the cover, so we can just get it
        AssetFileInfo coverInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).First();
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);

        // Set the name to {mapName}_Cover_2x
        coverBase["m_Name"].AsString = $"{context.SongData.Name}_Title";

        // Make it fit in 1024x512
        if (image.Width / (float)image.Height != 2f)
        {
            // Pad the image to 2:1
            int newWidth = image.Height * 2;
            image.Mutate(x => x.Pad(newWidth, image.Height));
        }

        image.Mutate(x => x.Resize(1024, 512));

        // Now we can encode the image
        {
            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            int mips = 1;

            encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips) ?? throw new Exception("Failed to encode image!");

            // Set the image data
            coverBase["image data"].AsByteArray = encImageBytes;
            coverBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coverBase["m_StreamData"]["offset"].AsInt = 0;
            coverBase["m_StreamData"]["size"].AsInt = 0;
            coverBase["m_StreamData"]["path"].AsString = "";

            // Save the file
            coverInfo.SetNewData(coverBase);
        }

        // Get the sprite
        AssetFileInfo coverSpriteInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite).First();
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);

        // Set the name to {mapName}_Title
        coverSpriteBase["m_Name"].AsString = $"{context.SongData.Name}_Title";

        // Save the file
        coverSpriteInfo.SetNewData(coverSpriteBase);

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Write the file
        string outputPackagePath = fs.OutputFolders.SongTitleLogoFolder;
        bun.SaveAndCompress(outputPackagePath, context.Request.ExportType == ExportType.CustomServer);
    }
}
