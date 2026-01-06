using JustDanceEditor.Formats.JDI;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.Unity.Services;

public sealed class UnityAssetMaterializerService : IUnityAssetMaterializer
{
    private readonly UnityAssetMaterializer _materializer;

    public UnityAssetMaterializerService(ILogger<UnityAssetMaterializerService> logger)
    {
        _materializer = new UnityAssetMaterializer(logger);
    }

    public void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot)
    {
        _materializer.Materialize(package, unityRoot, targetRoot);
    }
}