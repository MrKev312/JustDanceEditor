using JustDanceEditor.Formats.JDI;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.Unity.Services;

public sealed class UnityAssetMaterializerService(ILogger<UnityAssetMaterializerService> logger) : IUnityAssetMaterializer
{
    private readonly UnityAssetMaterializer _materializer = new(logger);

    public void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot)
    {
        _materializer.Materialize(package, unityRoot, targetRoot);
    }
}