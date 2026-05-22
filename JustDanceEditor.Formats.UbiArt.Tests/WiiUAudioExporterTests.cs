using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class WiiUAudioExporterTests
{
    [Theory]
    [InlineData(UbiArtEngineVersion.JD2016, 10U)]
    [InlineData(UbiArtEngineVersion.JD2019, 11U)]
    public async Task WriteAudioAsync_EncodesAmbAudioAsCafeAdpcm(UbiArtEngineVersion engineVersion, uint expectedRakiVersion)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceWav = Path.Combine(root, "amb.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSilentWave(sourceWav);

            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem(), engineVersion);
            WiiUCookedPlatformExporter exporter = new();

            await exporter.WriteAudioAsync(context, Path.Combine("cache", "itf_cooked", "wiiu", "world", "maps", "song", "audio", "amb", "amb_song_intro.wav"), sourceWav);

            string outputPath = Path.Combine(root, "cache", "itf_cooked", "wiiu", "world", "maps", "song", "audio", "amb", "amb_song_intro.wav.ckd");
            Assert.True(File.Exists(outputPath));
            Assert.Equal("Cafe", ReadAscii(outputPath, 8, 4));
            Assert.Equal("adpc", ReadRakiType(outputPath));
            Assert.Equal(expectedRakiVersion, ReadUInt32BE(outputPath, 4));
            Assert.Equal(5U, ReadUInt32BE(outputPath, 24));
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

    [Theory]
    [InlineData("song_cover_online.tga", 256, 256, 0x31U)]
    [InlineData("song_banner_bkg.tga", 1024, 512, 0x31U)]
    [InlineData("song_cover_albumcoach.tga", 512, 512, 0x33U)]
    [InlineData("timeline/pictos/picto.png", 256, 256, 0x33U)]
    public async Task WriteTextureAsync_UsesRetailBcFormatForTextureRole(
        string texturePath,
        int width,
        int height,
        uint expectedGx2Format)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            WiiUCookedPlatformExporter exporter = new();

            string relativePath = Path.Combine("cache", "itf_cooked", "wiiu", "world", "maps", "song", texturePath);
            using Image<Bgra32> image = new(width, height);

            await exporter.WriteTextureAsync(context, relativePath, image);

            byte[] output = File.ReadAllBytes(Path.Combine(root, relativePath + ".ckd"));
            Assert.Equal("TEX\0", Encoding.ASCII.GetString(output, 4, 4));

            uint payloadOffset = ReadUInt32BE(output, 8);
            Assert.Equal("Gfx2", Encoding.ASCII.GetString(output, (int)payloadOffset, 4));

            int surfaceOffset = checked((int)payloadOffset + 32 + 32);
            Assert.Equal((uint)width, ReadUInt32BE(output, surfaceOffset + 4));
            Assert.Equal((uint)height, ReadUInt32BE(output, surfaceOffset + 8));
            Assert.Equal(expectedGx2Format, ReadUInt32BE(output, surfaceOffset + 20));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static string ReadAscii(string path, int offset, int length)
        => Encoding.ASCII.GetString(File.ReadAllBytes(path), offset, length);

    private static uint ReadUInt32BE(string path, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(File.ReadAllBytes(path).AsSpan(offset, sizeof(uint)));

    private static uint ReadUInt32BE(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
}
