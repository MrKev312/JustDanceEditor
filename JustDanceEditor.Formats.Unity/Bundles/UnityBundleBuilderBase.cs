using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.Unity.Bundles.Extensions;

namespace JustDanceEditor.Formats.Unity.Bundles;

public abstract class UnityBundleBuilderBase
{
    protected (AssetsManager Manager, BundleFileInstance BunInst, AssetsFileInstance AFileInst, AssetsFile AFile, AssetFileInfo AssetBundleInfo, AssetTypeValueField AssetBundleBase, AssetFileInfo TextureInfo, AssetFileInfo SpriteInfo)
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

        assetBundleBase.SetString("m_Name", $"{codename}_SongTitleLogo");
        assetBundleBase.SetString("m_AssetBundleName", $"{codename}_SongTitleLogo");

        AssetFileInfo textureInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Texture2D);
        AssetFileInfo spriteInfo = sortedAssetInfos.First(x => x.TypeId == (int)AssetClassID.Sprite);

        return (manager, bunInst, afileInst, afile, assetBundleInfo, assetBundleBase, textureInfo, spriteInfo);
    }

    protected void FinalizeAndSaveBundle(string outputFolderPath, bool forCustomServer, AssetBundleFile bun, AssetsFile afile, AssetTypeValueField assetBundleBase, Action<AssetTypeValueField> setAssetBundleData)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        bun.SaveAndCompress(outputFolderPath, forCustomServer);
    }

    protected void ClearBundle(AssetsManager? manager)
    {
        if (manager == null) return;
        manager.UnloadAll();
    }
}