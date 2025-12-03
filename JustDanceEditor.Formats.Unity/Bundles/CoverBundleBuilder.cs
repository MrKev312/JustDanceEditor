using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using TextureConverter;
using TextureConverter.TextureConverterHelpers;

namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityCoverBundleRequest(
    string Codename,
    Image<Rgba32> CoverImage,
    string TemplatePath,
    string OutputFolderPath,
    bool ForCustomServer);

public static class CoverBundleBuilder
{
    public static void GenerateCoverBundle(UnityCoverBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        try
        {
            Logger.Log($"Starting generation for cover: {request.Codename}");

            request.CoverImage.Mutate(x => x.Resize(640, 360));

            (AssetsManager? manager, BundleFileInstance? bunInst, AssetsFileInstance? afileInst, AssetsFile? afile, AssetFileInfo? assetBundleInfo, AssetTypeValueField? assetBundleBase, AssetFileInfo? coverTextureInfo, AssetFileInfo? coverSpriteInfo) =
                InitializeBundle(request.TemplatePath, request.Codename);

            UpdateCoverTexture(request.Codename, manager, afileInst, coverTextureInfo, request.CoverImage);
            UpdateCoverSprite(request.Codename, manager, afileInst, coverSpriteInfo);

            FinalizeAndSaveBundle(request.OutputFolderPath, request.ForCustomServer, bunInst.file, afile, assetBundleBase, assetBundleInfo.SetNewData);

            Logger.Log($"Finished generating cover for {request.Codename}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to generate cover for {request.Codename}: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private static void ValidateRequest(UnityCoverBundleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Codename))
            throw new ArgumentException("Codename must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new ArgumentException("Template path must be provided.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputFolderPath))
            throw new ArgumentException("Output folder path must be provided.", nameof(request));
        if (!File.Exists(request.TemplatePath))
            throw new FileNotFoundException("Template bundle file not found.", request.TemplatePath);
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

    private static void UpdateCoverTexture(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverInfo, Image<Rgba32> coverImage)
    {
        AssetTypeValueField coverBase = manager.GetBaseField(afileInst, coverInfo);
        coverBase["m_Name"].AsString = $"{codename}_Cover_2x";

        TextureFormat fmt = TextureFormat.DXT1Crunched;
        int mips = 1;
        byte[] encImageBytes = TextureImportExport.Import(coverImage, fmt, out _, out _, ref mips) ?? throw new Exception("Failed to encode cover image!");

        coverBase["image data"].AsByteArray = encImageBytes;
        coverBase["m_CompleteImageSize"].AsUInt = (uint)encImageBytes.Length;
        coverBase["m_StreamData"]["offset"].AsInt = 0;
        coverBase["m_StreamData"]["size"].AsInt = 0;
        coverBase["m_StreamData"]["path"].AsString = string.Empty;

        coverInfo.SetNewData(coverBase);
    }

    private static void UpdateCoverSprite(string codename, AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo coverSpriteInfo)
    {
        AssetTypeValueField coverSpriteBase = manager.GetBaseField(afileInst, coverSpriteInfo);
        coverSpriteBase["m_Name"].AsString = $"{codename}_Cover_2x";
        coverSpriteInfo.SetNewData(coverSpriteBase);
    }

    private static void FinalizeAndSaveBundle(string outputFolderPath, bool forCustomServer, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bun.SaveAndCompress(outputFolderPath, forCustomServer);
    }
}
