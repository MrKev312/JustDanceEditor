using KevInc.UbiArt.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal sealed record LegacySkuActor(
    string Name,
    string FirstPathPart,
    string SecondPathPart,
    uint ResourceId,
    int ResourceIdOffset)
{
    public bool IsSongDescActor =>
        FirstPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase) ||
        SecondPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase);

    public bool FolderFirstPath =>
        UbiArtSkuBinaryPrimitives.LooksLikeFolder(FirstPathPart) &&
        !UbiArtSkuBinaryPrimitives.LooksLikeFolder(SecondPathPart);

    public string FullPath => FolderFirstPath
        ? FirstPathPart + SecondPathPart
        : SecondPathPart + FirstPathPart;
}
