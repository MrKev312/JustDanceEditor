using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

using JustDanceEditor.Converter.Unity;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using SwitchTexture.TextureConverterHelpers;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class SongTitleBundleGenerator
{
    public static void GenerateSongTitleLogo(ConvertUbiArtToUnity convert)
    {
        // Does the following exist?
        string logoPath = Path.Combine(convert.InputFolder, "songTitleLogo.png");
        if (!File.Exists(logoPath))
        {
            Console.WriteLine("No songTitleLogo.png found, skipping...");
            return;
        }

        string[] songTitleLogoPackagePaths = Directory.GetFiles(Path.Combine(convert.Template0Folder, "songTitleLogo"));
        if (songTitleLogoPackagePaths.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No songTitleLogo bundle found despite songTitleLogo.png existing, skipping...");
            Console.ResetColor();
            return;
        }

        string songTitleLogoPackagePath = songTitleLogoPackagePaths[0];

        Console.WriteLine("Converting SongTitleLogo...");

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
        assetBundleBase["m_Name"].AsString = $"{convert.SongData.Name}_SongTitleLogo";
        assetBundleBase["m_AssetBundleName"].AsString = $"{convert.SongData.Name}_SongTitleLogo";
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // There's only one texture2d in the cover, so we can just get it
        AssetFileInfo coverInfo = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).First();
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);

        // Set the name to {mapName}_Cover_2x
        coverBase["m_Name"].AsString = $"{convert.SongData.Name}_Title";

        // Load the image and make it fit in 1024x512
        Image<Rgba32> image = Image.Load<Rgba32>(logoPath);
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
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_map_bkg.tga.png");

            encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips, afile.Metadata.TargetPlatform, []) ?? throw new Exception("Failed to encode image!");

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
        coverSpriteBase["m_Name"].AsString = $"{convert.SongData.Name}_Title";

        // Save the file
        coverSpriteInfo.SetNewData(coverSpriteBase);

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Write the file
        string outputPackagePath = Path.Combine(convert.OutputFolder, "cache0", "songTitleLogo");
        bun.SaveAndCompress(outputPackagePath);
    }
}
