using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class PcLikeTextureExporterTests
{
    [Theory]
    [InlineData("pc", "song_banner_bkg.tga", 0x00011800U, 0x0000CCCCU)]
    [InlineData("pc", "song_cover_albumcoach.tga", 0x00012000U, 0x0000CCCCU)]
    [InlineData("pc", "timeline/pictos/picto.png", 0x00012000U, 0x0202CCCCU)]
    [InlineData("durango", "song_banner_bkg.tga", 0x00011800U, 0x0000CCCCU)]
    [InlineData("orbis", "song_banner_bkg.tga", 0x00011800U, 0x00000000U)]
    [InlineData("orbis", "timeline/pictos/picto.png", 0x00012000U, 0x00000000U)]
    [InlineData("orbis", "timeline/pictos/montage.png", 0x00012000U, 0x02020000U)]
    public async Task WriteTextureAsync_UsesRoleFormatFlagsAndPlatformSampler(
        string platformFolder,
        string textureName,
        uint expectedFormatFlags,
        uint expectedSamplerFlags)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            IPlatformExporter exporter = CreateExporter(platformFolder);

            string relativePath = Path.Combine("cache", "itf_cooked", platformFolder, "world", "maps", "song", "menuart", "textures", textureName);
            using Image<Bgra32> image = new(256, 256);

            await exporter.WriteTextureAsync(context, relativePath, image);

            byte[] output = File.ReadAllBytes(Path.Combine(root, relativePath + ".ckd"));
            Assert.Equal(expectedFormatFlags, ReadUInt32BE(output, 20));
            Assert.Equal(expectedSamplerFlags, ReadUInt32BE(output, 40));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static IPlatformExporter CreateExporter(string platformFolder) => platformFolder switch
    {
        "pc" => new PcCookedPlatformExporter(),
        "durango" => new DurangoCookedPlatformExporter(),
        "orbis" => new OrbisCookedPlatformExporter(),
        _ => throw new ArgumentOutOfRangeException(nameof(platformFolder), platformFolder, null)
    };

    private static uint ReadUInt32BE(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
}