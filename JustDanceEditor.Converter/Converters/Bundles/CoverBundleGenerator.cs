using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoverBundleGenerator
{
    public async static Task GenerateCoverAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => GenerateCover(convert));

    public static void GenerateCover(ConvertUbiArtToUnity convert)
    {
        try
        {
            GenerateCoverInternal(convert);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate cover: {e.Message}", LogLevel.Error);
        }
    }

    static void GenerateCoverInternal(ConvertUbiArtToUnity convert)
    {
        string coverPackagePath = Directory.GetFiles(Path.Combine(convert.TemplateFolder, "Cover"))[0];

        Logger.Log("Converting Cover...");

        // Open the coaches package using AssetTools.NET
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coverPackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];

        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{convert.SongData.Name}_Cover";
        assetBundleBase["m_AssetBundleName"].AsString = $"{convert.SongData.Name}_Cover";
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // There's only one texture2d in the cover, so we can just get it
        AssetFileInfo coverInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).First();
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);

        // Set the name to {mapName}_Cover_2x
        coverBase["m_Name"].AsString = $"{convert.SongData.Name}_Cover_2x";

        // If a cover.png exists in the map folder, use that
        Image<Rgba32>? coverImage = null;
        if (convert.ConversionRequest.OnlineCover)
            coverImage ??= CoverArtGenerator.TryCoverWeb(convert);
        coverImage ??= CoverArtGenerator.ExistingCover(convert);
        coverImage ??= CoverArtGenerator.GenerateOwnCover(convert);

        // Save the image in the temp folder
        coverImage.Save(Path.Combine(convert.TempMenuArtFolder, $"Cover_{convert.SongData.Name}.png"));

        // Now we can encode the image
        {
            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT1Crunched;
            int mips = 1;
            string path = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_map_bkg.tga.png");

            encImageBytes = TextureImportExport.Import(coverImage!, fmt, out int width, out int height, ref mips) ?? throw new Exception("Failed to encode image!");

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

        // Set the name to {mapName}_Cover_2x
        coverSpriteBase["m_Name"].AsString = $"{convert.SongData.Name}_Cover_2x";

        // Save the file
        coverSpriteInfo.SetNewData(coverSpriteBase);

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Write the file
        string outputPackagePath = Path.Combine(convert.Output0Folder, "Cover");
        bun.SaveAndCompress(outputPackagePath);
    }
}
