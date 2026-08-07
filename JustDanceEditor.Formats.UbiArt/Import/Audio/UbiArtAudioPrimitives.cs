using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Import.Audio;

internal static class UbiArtAudioPrimitives
{
    public static string? BuildAudioOffsetFilter(float offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) <= 0.000001f)
            return null;

        if (offsetSeconds > 0)
        {
            int delayMs = (int)Math.Round(offsetSeconds * 1000, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"adelay={delayMs}:all=1");
        }

        return string.Create(CultureInfo.InvariantCulture, $"atrim=start={-offsetSeconds},asetpts=PTS-STARTPTS");
    }

    public static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
            return 0;

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }
}
