using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.Unity.Services;

public interface IUnityAssetMaterializer
{
    void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot);
}