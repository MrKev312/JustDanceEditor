using JustDanceEditor.Conversion.Abstractions;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt;

internal static class UbiArtSupportStatus
{
    public static ConversionSupportStatus ForPlatform(UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Durango => ConversionSupportStatus.KnownPartial,
        _ => ConversionSupportStatus.Stable
    };
}
