using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace JustDanceEditor.Formats.Unity.Bundles;

public abstract class UnityBundleBuilderBase
{
    protected void FinalizeAndSaveBundle(
        string outputFolderPath,
        bool forCustomServer,
        AssetBundleFile bun,
        AssetsFile afile,
        AssetTypeValueField assetBundleBase,
        Action<AssetTypeValueField> setAssetBundleData,
        UnityBundlePublishTarget? publishTarget = null)
    {
        setAssetBundleData(assetBundleBase);
        bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        SaveFinalBundle(bun, outputFolderPath, forCustomServer, publishTarget);
    }

    protected static void SaveFinalBundle(AssetBundleFile bun, string outputFolderPath, bool forCustomServer, UnityBundlePublishTarget? publishTarget)
    {
        if (publishTarget == null)
        {
            bun.SaveAndCompress(outputFolderPath, forCustomServer);
            return;
        }

        UnityBundlePublisher.Publish(bun, publishTarget, forCustomServer);
    }

    protected void ClearBundle(AssetsManager? manager)
    {
        if (manager == null)
            return;
        manager.UnloadAll(true);
    }
}