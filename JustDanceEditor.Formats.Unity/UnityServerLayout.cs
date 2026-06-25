namespace JustDanceEditor.Formats.Unity;

internal static class UnityServerLayout
{
    public static string GetBundleFolder(string mapRoot, string bundleFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleFolderName);

        foreach (UnityServerPlatform platform in UnityServerPlatforms.ImportSourcePriority)
        {
            string platformFolder = Path.Combine(mapRoot, platform.FolderName, bundleFolderName);
            if (Directory.Exists(platformFolder))
                return platformFolder;
        }

        string legacyFolder = Path.Combine(mapRoot, bundleFolderName);
        if (Directory.Exists(legacyFolder))
            return legacyFolder;

        return Path.Combine(mapRoot, UnityServerPlatforms.ImportSourcePriority[0].FolderName, bundleFolderName);
    }
}