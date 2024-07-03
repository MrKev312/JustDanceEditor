using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using AssetsTools.NET;

using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Converter.Unity.TextureConverter;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Converter.Converters.Bundles;

public static class CoachesSmallBundleGenerator
{
    public static void GenerateCoachesSmall(ConvertUbiArtToUnity convert)
    {
        // Now we do the same for the coaches small
        string coacheSmallPackagePath = Directory.GetFiles(Path.Combine("./template", "cachex", "CoachesSmall"))[0];

        Console.WriteLine("Converting CoachesSmall...");

        // Open the coaches package using AssetTools.NET
        AssetsManager manager = new();
        BundleFileInstance bunInst = manager.LoadBundleFile(coacheSmallPackagePath, true);
        AssetBundleFile bun = bunInst.file;
        AssetsFileInstance afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
        AssetsFile afile = afileInst.file;
        afile.GenerateQuickLookup();

        List<AssetFileInfo> sortedAssetInfos = [.. afile.AssetInfos.OrderBy(x => x.TypeId)];
        AssetFileInfo assetBundle = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.AssetBundle).First();
        AssetTypeValueField assetBundleBase = manager.GetBaseField(afileInst, assetBundle);
        assetBundleBase["m_Name"].AsString = $"{convert.SongData.Name}_CoachesSmall";
        assetBundleBase["m_AssetBundleName"].AsString = $"{convert.SongData.Name}_CoachesSmall";
        AssetTypeValueField assetBundleArray = assetBundleBase["m_PreloadTable"]["Array"];

        // Get all texture2d's
        AssetFileInfo[] textureInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Texture2D).ToArray();
        AssetFileInfo[] spriteInfos = sortedAssetInfos.Where(x => x.TypeId == (int)AssetClassID.Sprite).ToArray();

        foreach (AssetFileInfo coachInfo in textureInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            byte[] encImageBytes;
            TextureFormat fmt = TextureFormat.DXT5Crunched;
            byte[] platformBlob = [];
            uint platform = afile.Metadata.TargetPlatform;
            int mips = 1;
            string path = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_coach_1.tga.png");
            Image<Rgba32> image;

            // If the name does not end with 1_Phone, delete it
            if (!coachName.EndsWith("1_Phone"))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            // Load the image
            image = Image.Load<Rgba32>(path);

            // Resize the image to 256x256
            image.Mutate(x => x.Resize(256, 256));

            encImageBytes = TextureImportExport.Import(image, fmt, out int width, out int height, ref mips, platform, platformBlob) ?? throw new Exception("Failed to encode");

            // Set the name to {mapName}_Coach_1_Phone
            coachBase["m_Name"].AsString = $"{convert.SongData.Name}_Coach_1_Phone";

            // Set the image data
            coachBase["image data"].AsByteArray = encImageBytes;
            coachBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
            coachBase["m_StreamData"]["offset"].AsULong = 0;
            coachBase["m_StreamData"]["size"].AsUInt = 0;
            coachBase["m_StreamData"]["path"].AsString = "";

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // For each sprite in the file
        foreach (AssetFileInfo coachInfo in spriteInfos)
        {
            AssetTypeValueField coachBase = manager.GetBaseField(afileInst, coachInfo);

            // Get the name
            string coachName = coachBase["m_Name"].AsString;

            // If the name does not end with 1_Phone, delete it
            if (!coachName.EndsWith("1_Phone"))
            {
                // Remove the coach from the bundle
                afile.AssetInfos.Remove(coachInfo);

                // And remove it from the preload table
                assetBundleArray.Children.Remove(assetBundleArray.Children.Where(x => x["m_PathID"].AsLong == coachInfo.PathId).First());

                continue;
            }

            // Set the name to {mapName}_Coach_1_Phone
            coachBase["m_Name"].AsString = $"{convert.SongData.Name}_Coach_1_Phone";

            // Save the file
            coachInfo.SetNewData(coachBase);
        }

        // Apply changes to the AssetBundle
        assetBundle.SetNewData(assetBundleBase);

        // Save the file
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

        // Add .mod to the end of the file
        string outputPackagePath = Path.Combine(convert.OutputFolder, "cachex", "CoachesSmall");
        bun.SaveAndCompress(outputPackagePath);
    }
}
