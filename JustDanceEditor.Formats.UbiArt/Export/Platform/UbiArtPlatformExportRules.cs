using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

internal static class UbiArtPlatformExportRules
{
    public static string GetAdpcmPlatform(UbiArtPlatform platform) =>
        platform == UbiArtPlatform.Revolution ? "Wii " : "Cafe";

    public static uint GetAdpcmVersion(UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        if (engineVersion == UbiArtEngineVersion.JD2014)
            return 8;

        if (platform == UbiArtPlatform.Revolution)
            return 9;

        return engineVersion switch
        {
            UbiArtEngineVersion.JD2015 => 9,
            UbiArtEngineVersion.JD2016 => 10,
            UbiArtEngineVersion.Unknown => 0x0B,
            _ => 0x0B
        };
    }

    public static bool IsAmbAudio(string relativePath) =>
        Path.GetFileName(relativePath).StartsWith("amb_", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldUseAlphaTexture(string relativePath, Image<Bgra32> image)
    {
        string path = Normalize(relativePath);

        if (path.Contains("_banner_bkg.", StringComparison.Ordinal) ||
            path.Contains("_map_bkg.", StringComparison.Ordinal) ||
            path.Contains("_cover_albumbkg.", StringComparison.Ordinal) ||
            path.Contains("_cover_online", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains("/timeline/pictos/", StringComparison.Ordinal) ||
            path.Contains("_cover_albumcoach.", StringComparison.Ordinal) ||
            (path.Contains("/world/maps/", StringComparison.Ordinal) && path.Contains("_coach_", StringComparison.Ordinal)))
        {
            return true;
        }

        return HasTransparency(image);
    }

    public static bool UsesLogicalOnlineCoverDimensions(string relativePath) =>
        Normalize(relativePath).Contains("_cover_online", StringComparison.Ordinal);

    public static bool IsPictogram(string relativePath) =>
        Normalize(relativePath).Contains("/timeline/pictos/", StringComparison.Ordinal);

    public static uint GetNxSamplerFlags(string relativePath) =>
        IsPictogram(relativePath) ? 0x02020000U : 0U;

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').ToLowerInvariant();

    private static bool HasTransparency(Image<Bgra32> image)
    {
        bool hasAlpha = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasAlpha; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 255)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }
        });

        return hasAlpha;
    }
}