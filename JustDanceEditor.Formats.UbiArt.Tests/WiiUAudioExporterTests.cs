using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using NAudio.Wave;

using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class WiiUAudioExporterTests
{
    [Fact]
    public async Task WriteAudioAsync_EncodesAmbAudioAsCafeAdpcm()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceWav = Path.Combine(root, "amb.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSilentWave(sourceWav);

            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            WiiUCookedPlatformExporter exporter = new();

            await exporter.WriteAudioAsync(context, Path.Combine("cache", "itf_cooked", "wiiu", "world", "maps", "song", "audio", "amb", "amb_song_intro.wav"), sourceWav);

            string outputPath = Path.Combine(root, "cache", "itf_cooked", "wiiu", "world", "maps", "song", "audio", "amb", "amb_song_intro.wav.ckd");
            Assert.True(File.Exists(outputPath));
            Assert.Equal("adpc", ReadRakiType(outputPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void WriteSilentWave(string path)
    {
        WaveFormat format = new(48000, 16, 2);
        using WaveFileWriter writer = new(path, format);
        byte[] buffer = new byte[format.AverageBytesPerSecond / 10];
        writer.Write(buffer, 0, buffer.Length);
    }

    private static string ReadRakiType(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = Encoding.ASCII.GetString(bytes, 0, 4) == "RAKI" ? 0 : 4;
        return Encoding.ASCII.GetString(bytes, offset + 12, 4);
    }
}
