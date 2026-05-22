using AssetsTools.NET;
using AssetsTools.NET.Extra;

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
        Publish(generatedBundle, target, keepExtension);
    }

    public static void Publish(byte[] generatedBundle, UnityBundlePublishTarget target, bool keepExtension)
    {
        ArgumentNullException.ThrowIfNull(generatedBundle);
        ArgumentNullException.ThrowIfNull(target);

        foreach (UnityServerPlatform platform in target.Platforms)
        {
            byte[] platformBundle = UnityBundlePlatformConverter.Convert(generatedBundle, platform.BuildTarget);
            string outputFolder = Path.Combine(target.OutputRoot, platform.FolderName, target.BundleFolderName);
            WriteBundle(platformBundle, outputFolder, keepExtension);
        }
    }

    public static void WriteBundle(byte[] bundle, string outputFolder, bool keepExtension)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

        using MemoryStream stream = new(bundle, writable: false);
        AssetsManager manager = new();
        try
        {
            BundleFileInstance bundleInstance = manager.LoadBundleFile(stream, "generated.bundle", true);
            bundleInstance.file.SaveAndCompress(outputFolder, keepExtension);
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }
}
