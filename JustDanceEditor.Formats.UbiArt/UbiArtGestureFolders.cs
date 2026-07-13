using JustDanceEditor.Formats.JDI;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt;

internal static class UbiArtGestureFolders
{
    public const string Durango = "durango";
    public const string Orbis = "orbis";
    public const string X360 = "x360";

    public static UbiArtGestureFolder[] All { get; } =
    [
        new(UbiArtPlatform.Durango, Durango),
        new(UbiArtPlatform.Orbis, Orbis),
        new(UbiArtPlatform.Xenon, X360)
    ];

    public static string PackageFolder(string platformFolder) =>
        IntermediatePackageLayout.Assets.GestureFolder(platformFolder);

    public static bool TryGetPlatformFolder(UbiArtPlatform platform, out string? platformFolder)
    {
        foreach (UbiArtGestureFolder gestureFolder in All)
        {
            if (gestureFolder.Platform == platform)
            {
                platformFolder = gestureFolder.PlatformFolder;
                return true;
            }
        }

        platformFolder = null;
        return false;
    }
}

internal readonly record struct UbiArtGestureFolder(UbiArtPlatform Platform, string PlatformFolder)
{
    public string PackageRelativeFolder => UbiArtGestureFolders.PackageFolder(PlatformFolder);
}