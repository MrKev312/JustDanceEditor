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

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class SongTitleBundleGenerator
{
    public async static Task GenerateSongTitleLogoAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => GenerateSongTitleLogo(convert));

    public static void GenerateSongTitleLogo(ConvertUbiArtToUnity convert)
    {
        try
        {
            GenerateSongTitleLogoInternal(convert);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate song title logo: {e.Message}", LogLevel.Error);
        }
    }

    static void GenerateSongTitleLogoInternal(ConvertUbiArtToUnity convert)
    {
        // Does the following exist?
        //string logoPath = Path.Combine(convert.InputMenuArtFolder, "songTitleLogo.png");
        FileSystem fs = convert.FileSystem;

        if (fs.GetFilePath(Path.Combine(fs.InputFolders.MenuArtFolder), out CookedFile? logoPath) || logoPath == null)
        {
            //Console.WriteLine("No songTitleLogo.png found, skipping...");
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
        coverSpriteBase["m_Name"].AsString = $"{convert.SongData.Name}_Title";

        // Save the file
        coverSpriteInfo.SetNewData(coverSpriteBase);

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Write the file
        string outputPackagePath = fs.OutputFolders.SongTitleLogoFolder;
        bun.SaveAndCompress(outputPackagePath);
    }
}
