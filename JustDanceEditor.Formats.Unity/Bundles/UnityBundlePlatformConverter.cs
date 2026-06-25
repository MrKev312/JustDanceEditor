using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace JustDanceEditor.Formats.Unity.Bundles;

internal static class UnityBundlePlatformConverter
{
    public static byte[] Convert(byte[] sourceBundle, uint targetPlatform)
    {
        ArgumentNullException.ThrowIfNull(sourceBundle);

        using MemoryStream sourceStream = new(sourceBundle, writable: false);
        AssetsManager manager = new();
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(sourceStream, "generated.bundle", true);
            AssetBundleFile bundleFile = bundle.file;
            bool changed = false;

            for (int i = 0; i < bundleFile.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (!bundleFile.IsAssetsFile(i))
                    continue;

                AssetBundleDirectoryInfo dirInfo = bundleFile.BlockAndDirInfo.DirectoryInfos[i];
                AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, i, false);
                if (assets.file.Metadata.TargetPlatform == targetPlatform)
                    continue;

                assets.file.Metadata.TargetPlatform = targetPlatform;
                dirInfo.SetNewData(assets.file);
                changed = true;
            }

            if (!changed)
                return sourceBundle;

            using MemoryStream uncompressedStream = new();
            using (AssetsFileWriter writer = new(UnityAssetExtensions.WrapNonClosing(uncompressedStream)))
            {
                bundleFile.Write(writer);
            }

            uncompressedStream.Position = 0;

            AssetBundleFile rewrittenBundle = new();
            rewrittenBundle.Read(new AssetsFileReader(uncompressedStream));

            try
            {
                using MemoryStream outputStream = new();
                using (AssetsFileWriter writer = new(outputStream))
                {
                    rewrittenBundle.Pack(writer, AssetBundleCompressionType.LZ4, true, null);
                }

                return outputStream.ToArray();
            }
            finally
            {
                rewrittenBundle.Close();
            }
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }
}