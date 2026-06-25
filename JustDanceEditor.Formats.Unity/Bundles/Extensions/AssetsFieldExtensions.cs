using AssetsTools.NET;

namespace JustDanceEditor.Formats.Unity.Bundles.Extensions;

public static class AssetsFieldExtensions
{
    public static AssetTypeValueField GetOrAdd(this AssetTypeValueField root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrEmpty(path))
            return root;

        string[] parts = path.Split('.');
        AssetTypeValueField cur = root;
        foreach (string part in parts)
        {
            cur = cur[part];
        }

        return cur;
    }

    public static void SetString(this AssetTypeValueField root, string path, string value) => root.GetOrAdd(path).AsString = value;
    public static void SetBytes(this AssetTypeValueField root, string path, byte[] value) => root.GetOrAdd(path).AsByteArray = value;
    public static void SetUInt(this AssetTypeValueField root, string path, uint value) => root.GetOrAdd(path).AsUInt = value;
}