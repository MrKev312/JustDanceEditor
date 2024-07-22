using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Converter.Unity.TextureConverter;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoverBundleGenerator
{
    public static void GenerateCover(ConvertUbiArtToUnity convert)
    {
        string coverPackagePath = Directory.GetFiles(Path.Combine(convert.Template0Folder, "Cover"))[0];

        Console.WriteLine("Converting Cover...");

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
        Image<Rgba32>? coverImage = CoverArtGenerator.ExistingCover(convert);
        if (convert.ConversionRequest.OnlineCover)
            coverImage ??= CoverArtGenerator.TryCoverWeb(convert);
        coverImage ??= CoverArtGenerator.GenerateOwnCover(convert);

        // Save the image in the temp folder
        coverImage.Save(Path.Combine(convert.TempMenuArtFolder, $"Cover_{convert.SongData.Name}.png"));

        // Now we can encode the image
        {
            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT1Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_map_bkg.tga.png");

            encImageBytes = TextureImportExport.Import(coverImage!, fmt, out int width, out int height, ref mips, afile.Metadata.TargetPlatform, []) ?? throw new Exception("Failed to encode image!");

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
        string outputPackagePath = Path.Combine(convert.OutputFolder, "cache0", "Cover");
        bun.SaveAndCompress(outputPackagePath);
    }
}
