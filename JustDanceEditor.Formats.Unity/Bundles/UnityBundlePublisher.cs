using AssetsTools.NET;

namespace JustDanceEditor.Formats.Unity.Bundles;

internal static class UnityBundlePublisher
{
    public static void Publish(AssetBundleFile bundle, UnityBundlePublishTarget target, bool keepExtension)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(target.OutputRoot))
            throw new ArgumentException("Output root must be provided.", nameof(target));
        if (string.IsNullOrWhiteSpace(target.BundleFolderName))
            throw new ArgumentException("Bundle folder name must be provided.", nameof(target));
        if (target.Platforms.Count == 0)
            throw new ArgumentException("At least one Unity server platform must be provided.", nameof(target));

        byte[] generatedBundle = bundle.ToCompressedBundleData();

        foreach (UnityServerPlatform platform in target.Platforms)
        {
            byte[] platformBundle = UnityBundlePlatformConverter.Convert(generatedBundle, platform.BuildTarget);
            string outputFolder = Path.Combine(target.OutputRoot, platform.FolderName, target.BundleFolderName);
            UnityAssetExtensions.WriteCompressedBundle(platformBundle, outputFolder, keepExtension);
        }
    }
}
