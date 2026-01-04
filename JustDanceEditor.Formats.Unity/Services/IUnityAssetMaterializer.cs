namespace JustDanceEditor.Formats.Unity.Services;

using JustDanceEditor.Formats.JDI;

public interface IUnityAssetMaterializer
{
    void Materialize(IntermediateSongPackage package, string unityRoot, string targetRoot);
}