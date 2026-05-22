using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using NAudio.Wave;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class WiiAudioExporterTests
{
    [Theory]
    [InlineData("song.wav", 4U, 3U)]
    [InlineData("amb/amb_song_intro.wav", 5U, 0U)]
    public async Task WriteAudioAsync_Jd2020_UsesRetailWiiAdpcmHeader(string relativeAudioPath, uint expectedChunks, uint expectedUnknown)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceWav = Path.Combine(root, "source.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSilentWave(sourceWav);

            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem(), UbiArtEngineVersion.JD2020);
            WiiCookedPlatformExporter exporter = new();

            await exporter.WriteAudioAsync(
                context,
                Path.Combine("cache", "itf_cooked", "wii", "world", "maps", "song", "audio", relativeAudioPath),
                sourceWav);

            string outputPath = Path.Combine(root, "cache", "itf_cooked", "wii", "world", "maps", "song", "audio", relativeAudioPath + ".ckd");
            Assert.True(File.Exists(outputPath));
            byte[] output = File.ReadAllBytes(outputPath);

            Assert.Equal("RAKI", Encoding.ASCII.GetString(output, 0, 4));
            Assert.Equal(9U, ReadUInt32BE(output, 4));
            Assert.Equal("Wii ", Encoding.ASCII.GetString(output, 8, 4));
            Assert.Equal("adpc", Encoding.ASCII.GetString(output, 12, 4));
            Assert.Equal(0x122U + (expectedChunks == 5 ? 0x0CU : 0U), ReadUInt32BE(output, 16));
            Assert.Equal(0x140U, ReadUInt32BE(output, 20));
            Assert.Equal(expectedChunks, ReadUInt32BE(output, 24));
            Assert.Equal(expectedUnknown, ReadUInt32BE(output, 28));
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

    private static uint ReadUInt32BE(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
}
