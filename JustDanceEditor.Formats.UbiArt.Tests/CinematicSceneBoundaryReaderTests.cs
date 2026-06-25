using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicSceneBoundaryReaderTests
{
    [Fact]
    public void Mesh3DPathScan_IgnoresMeshDirectoryRecord()
    {
        using MemoryStream stream = new();
        WritePath("world/jd5/test/graph/textures/", "glow.tga");
        WritePath("world/jd5/test/graph/materials/", "glow.msh");
        WritePath("world/jd5/test/graph/mesh/", "\0");
        WritePath("world/jd5/test/graph/mesh/", "paving_l02_r1.m3d");
        byte[] bytes = stream.ToArray();
        CinematicBinaryReader reader = new(bytes);

        bool found = CinematicTemplateVisualResolver.TryFindMesh3DPathRecords(
            reader,
            searchStartOffset: 0,
            searchLength: bytes.Length,
            out IReadOnlyList<string> texturePaths,
            out string? materialPath,
            out string? meshPath,
            out int componentEndOffset);

        Assert.True(found);
        Assert.Equal("world/jd5/test/graph/textures/glow.tga", Assert.Single(texturePaths));
        Assert.Equal("world/jd5/test/graph/materials/glow.msh", materialPath);
        Assert.Equal("world/jd5/test/graph/mesh/paving_l02_r1.m3d", meshPath);
        Assert.Equal(bytes.Length, componentEndOffset);

        void WritePath(string folder, string file)
        {
            WriteString(folder);
            WriteString(file);
            Span<byte> hash = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(hash, 0);
            stream.Write(hash);
        }

        void WriteString(string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, text.Length);
            stream.Write(length);
            stream.Write(text);
        }
    }


    [Fact]
    public void Mesh3DLayout_Jd2014ReadsScaleZAfterGraphicComponentHeader()
    {
        byte[] bytes = new byte[128];
        WriteSingle(bytes, 48, 1.0f);
        WriteSingle(bytes, CinematicConstants.Mesh3DScaleZOffset, 1.25f);

        float scaleZ = CinematicSceneBoundaryReader.ReadMesh3DScaleZOrDefault(
            bytes,
            0,
            new LegacyBinarySerializerContext(2014));

        AssertClose(1.25, scaleZ);
    }


    [Fact]
    public void Mesh3DLayout_Jd2015ReadsCompactScaleZ()
    {
        byte[] bytes = new byte[96];
        WriteSingle(bytes, CinematicConstants.Mesh3DScaleZOffset, 7.0f);
        WriteSingle(bytes, CinematicConstants.Mesh3DScaleZOffset2015, 2.5f);

        float scaleZ = CinematicSceneBoundaryReader.ReadMesh3DScaleZOrDefault(
            bytes,
            0,
            new LegacyBinarySerializerContext(2015));

        AssertClose(2.5, scaleZ);
    }
}