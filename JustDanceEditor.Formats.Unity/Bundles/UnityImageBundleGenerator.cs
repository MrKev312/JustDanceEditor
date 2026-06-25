using JustDanceEditor.Formats.Unity.Bundles.Synthesis;

using KevInc.Texture;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles;

internal sealed record UnityImageBundleAsset(
    string ContainerName,
    string TextureName,
    string SpriteName,
    Image<Rgba32> Image,
    int Width,
    int Height,
    TextureFormat TextureFormat);

internal sealed record UnityImageBundleRequest(
    string BundleName,
    string OutputFolderPath,
    bool ForCustomServer,
    IReadOnlyList<UnityImageBundleAsset> Assets,
    UnityBundlePublishTarget? PublishTarget);

internal static class UnityImageBundleGenerator
{
    public static void Generate(UnityImageBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BundleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolderPath);

        if (request.Assets.Count == 0)
            throw new ArgumentException("At least one image asset is required.", nameof(request));

        using Stream classData = UnityClassDataProvider.OpenClassPackageStream();
        byte[] bundle = UnitySyntheticBundleFactory.CreateImageBundle(
            classData,
            request.BundleName,
            request.Assets);

        SaveBundle(bundle, request);
    }

    private static void SaveBundle(byte[] bundle, UnityImageBundleRequest request)
    {
        if (request.PublishTarget == null)
        {
            UnityBundlePublisher.WriteBundle(bundle, request.OutputFolderPath, request.ForCustomServer);
            return;
        }

        UnityBundlePublisher.Publish(bundle, request.PublishTarget, request.ForCustomServer);
    }
}