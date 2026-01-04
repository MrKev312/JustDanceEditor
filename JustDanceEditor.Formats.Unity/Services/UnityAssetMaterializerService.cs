using JustDanceEditor.Formats.JDI;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.Unity.Services;

public sealed class UnityAssetMaterializerService(ILogger<UnityAssetMaterializerService> logger) : IUnityAssetMaterializer
{
    private readonly ILogger<UnityAssetMaterializerService> _logger = logger;

    public void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot)
    {
        // Forward to the static helper but pass the injected logger
        UnityAssetMaterializer.Materialize(package, unityRoot, targetRoot, _logger);
    }
}