namespace JustDanceEditor.Formats.Unity.Bundles;

public sealed record UnityBundlePublishTarget(
    string OutputRoot,
    string BundleFolderName,
    IReadOnlyList<UnityServerPlatform> Platforms);
