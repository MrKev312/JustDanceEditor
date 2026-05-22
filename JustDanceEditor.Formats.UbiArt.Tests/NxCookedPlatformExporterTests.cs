using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class NxCookedPlatformExporterTests
{
    [Fact]
    public async Task WriteTextureAsync_MenuBanner_UsesRetailDxt1NxShape()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);
            NxCookedPlatformExporter exporter = new();
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            string relativePath = Path.Combine("cache", "itf_cooked", "nx", "world", "maps", "aqueda", "menuart", "textures", "aqueda_banner_bkg.tga");
            using Image<Bgra32> image = new(1024, 512);

            await exporter.WriteTextureAsync(context, relativePath, image);

            byte[] bytes = File.ReadAllBytes(Path.Combine(root, relativePath + ".ckd"));
            Assert.Equal(0x00040080U, ReadUInt32BE(bytes, 12));
            Assert.Equal(1024, ReadUInt16BE(bytes, 16));
            Assert.Equal(512, ReadUInt16BE(bytes, 18));
            Assert.Equal(0x00011800U, ReadUInt32BE(bytes, 20));
            Assert.Equal(0x00080000U, ReadUInt32BE(bytes, 32));
            Assert.Equal(0U, ReadUInt32BE(bytes, 40));
            Assert.Equal(66U, ReadXtxFormat(bytes));
            Assert.Equal(262144UL, ReadXtxDataSize(bytes));
            Assert.Equal(7U, ReadXtxLayout2(bytes));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteTextureAsync_MenuOnlineCover_UsesRetailDxt1LogicalCoverShape()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);
            NxCookedPlatformExporter exporter = new();
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            string relativePath = Path.Combine("cache", "itf_cooked", "nx", "world", "maps", "aqueda", "menuart", "textures", "aqueda_cover_online.tga");
            using Image<Bgra32> image = new(256, 256);

            await exporter.WriteTextureAsync(context, relativePath, image);

            byte[] bytes = File.ReadAllBytes(Path.Combine(root, relativePath + ".ckd"));
            Assert.Equal(0x00008080U, ReadUInt32BE(bytes, 12));
            Assert.Equal(1024, ReadUInt16BE(bytes, 16));
            Assert.Equal(1024, ReadUInt16BE(bytes, 18));
            Assert.Equal(0x00011800U, ReadUInt32BE(bytes, 20));
            Assert.Equal(0x00100000U, ReadUInt32BE(bytes, 32));
            Assert.Equal(0U, ReadUInt32BE(bytes, 40));
            Assert.Equal(66U, ReadXtxFormat(bytes));
            Assert.Equal(32768UL, ReadXtxDataSize(bytes));
            Assert.Equal(7U, ReadXtxLayout2(bytes));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteTextureAsync_MenuAlbumCoach_KeepsAlphaCapableDxt5()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);
            NxCookedPlatformExporter exporter = new();
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());
            string relativePath = Path.Combine("cache", "itf_cooked", "nx", "world", "maps", "aqueda", "menuart", "textures", "aqueda_cover_albumcoach.tga");
            using Image<Bgra32> image = new(64, 64);

            await exporter.WriteTextureAsync(context, relativePath, image);

            byte[] bytes = File.ReadAllBytes(Path.Combine(root, relativePath + ".ckd"));
            Assert.Equal(0x00012000U, ReadUInt32BE(bytes, 20));
            Assert.Equal(68U, ReadXtxFormat(bytes));
            Assert.Equal(7U, ReadXtxLayout2(bytes));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static uint ReadUInt32BE(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));

    private static ushort ReadUInt16BE(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, sizeof(ushort)));

    private static ulong ReadXtxDataSize(byte[] bytes)
        => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(GetXtxTextureHeaderOffset(bytes), sizeof(ulong)));

    private static uint ReadXtxFormat(byte[] bytes)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(GetXtxTextureHeaderOffset(bytes) + 28, sizeof(uint)));

    private static uint ReadXtxLayout2(byte[] bytes)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(GetXtxTextureHeaderOffset(bytes) + 112, sizeof(uint)));

    private static int GetXtxTextureHeaderOffset(byte[] bytes)
    {
        Assert.Equal("TEX\0", Encoding.ASCII.GetString(bytes, 4, 4));
        int payloadOffset = (int)ReadUInt32BE(bytes, 8);
        int textureBlockOffset = payloadOffset + 16;
        long textureHeaderRelativeOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(textureBlockOffset + 16, sizeof(long)));
        return checked(textureBlockOffset + (int)textureHeaderRelativeOffset);
    }
}
