using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

public static class ColorThemeGenerator
{
    public record SongTheme(Color Color1A, Color Color1B, Color Color2A, Color Color2B);

    public static SongTheme GenerateFromImage(Image<Bgra32> image)
    {
        // 1. Resize to small size for fast processing (64x64)
        using Image<Bgra32> analysisImg = image.Clone(x => x.Resize(64, 64));

        List<HsvPixel> validPixels = [];

        // 2. Extract valid pixels
        analysisImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Bgra32 p = row[x];

                    if (p.A < 200)
                        continue; // Ignore transparent

                    // Manual Conversion here
                    RgbToHsv(p.R, p.G, p.B, out float h, out float s, out float v);

                    // Filter: S > 0.3 (Color exists), V > 0.2 (Not black), V < 0.95 (Not white)
                    if (s > 0.3f && v > 0.2f && v < 0.95f)
                    {
                        validPixels.Add(new HsvPixel(h, s, v));
                    }
                }
            }
        });

        if (validPixels.Count == 0)
            return DefaultTheme();

        // 3. Cluster by Hue (30 degree buckets)
        List<IGrouping<int, HsvPixel>> clusters = [.. validPixels
            .GroupBy(p => (int)(p.H / 30))
            .OrderByDescending(g => g.Count())];

        // 4. Select Primary Color (Largest cluster)
        HsvPixel primaryHsv = GetAverageHsv(clusters[0]);

        // 5. Select Secondary Color
        HsvPixel secondaryHsv;
        // Find cluster > 45 degrees away
        IGrouping<int, HsvPixel>? contrastingCluster = clusters.FirstOrDefault(g => Math.Abs(GetAverageHsv(g).H - primaryHsv.H) > 45);

        if (contrastingCluster != null)
        {
            secondaryHsv = GetAverageHsv(contrastingCluster);
        }
        else
        {
            // Monochromatic fallback: Complementary hue
            secondaryHsv = new HsvPixel((primaryHsv.H + 180) % 360, primaryHsv.S, primaryHsv.V);
        }

        // 6. Generate Gradients (B is slightly darker/saturated)
        return new SongTheme(
            Color1A: HsvToColor(primaryHsv),
            Color1B: HsvToColor(Shift(primaryHsv, 0, 0.1f, -0.2f)),
            Color2A: HsvToColor(secondaryHsv),
            Color2B: HsvToColor(Shift(secondaryHsv, 0, 0.1f, -0.2f))
        );
    }

    public static float[] ToUbiArtColor(Color c)
    {
        Rgba32 p = c.ToPixel<Rgba32>();
        return
        [
            p.A / 255f, // Alpha first
            p.R / 255f,
            p.G / 255f,
            p.B / 255f
        ];
    }

    private static SongTheme DefaultTheme()
    {
        return new SongTheme(Color.HotPink, Color.DeepPink, Color.Cyan, Color.DarkCyan);
    }

    private static HsvPixel Shift(HsvPixel p, float hDelta, float sDelta, float vDelta)
    {
        return new HsvPixel(
            (p.H + hDelta) % 360,
            Math.Clamp(p.S + sDelta, 0, 1),
            Math.Clamp(p.V + vDelta, 0, 1)
        );
    }

    private static HsvPixel GetAverageHsv(IEnumerable<HsvPixel> pixels)
    {
        float avgH = pixels.Average(p => p.H);
        float avgS = pixels.Average(p => p.S);
        float avgV = pixels.Average(p => p.V);
        return new HsvPixel(avgH, avgS, avgV);
    }

    // --- Math Helpers ---
    private struct HsvPixel(float h, float s, float v) { public float H = h; public float S = s; public float V = v; }

    private static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;

        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        v = max;
        s = (max == 0) ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else
        {
            if (max == rf)
                h = ((gf - bf) / delta) + (gf < bf ? 6 : 0);
            else if (max == gf)
                h = ((bf - rf) / delta) + 2;
            else
                h = ((rf - gf) / delta) + 4;
            h /= 6;
        }

        h *= 360f; // 0..360
    }

    private static Color HsvToColor(HsvPixel p)
    {
        float h = p.H;
        float s = p.S;
        float v = p.V;

        float c = v * s;
        float x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        float m = v - c;
        float r, g, b;

        if (h < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        byte rb = (byte)((r + m) * 255);
        byte gb = (byte)((g + m) * 255);
        byte bb = (byte)((b + m) * 255);

        return Color.FromRgb(rb, gb, bb);
    }
}