using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using NAudio.Wave;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class X360AudioExporterTests
{
    [Fact]
    public async Task WriteAudioAsync_EncodesWaveInputToX360Xma2Raki()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceWav = Path.Combine(root, "source.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSilentWave(sourceWav);

            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            X360CookedPlatformExporter exporter = new();

            await exporter.WriteAudioAsync(context, Path.Combine("cache", "itf_cooked", "x360", "world", "maps", "song", "audio", "song.wav"), sourceWav);

            string outputPath = Path.Combine(root, "cache", "itf_cooked", "x360", "world", "maps", "song", "audio", "song.wav.ckd");
            Assert.True(File.Exists(outputPath));
            Assert.Equal("RAKI", ReadAscii(outputPath, 0, 4));
            Assert.Equal("X360", ReadAscii(outputPath, 8, 4));
            Assert.Equal("xma2", ReadAscii(outputPath, 12, 4));
            Assert.True(new FileInfo(outputPath).Length > 0x800);
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

    private static string ReadAscii(string path, int offset, int length)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Encoding.ASCII.GetString(bytes, offset, length);
    }
}
