using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicMaterialGraphicReaderTests
{
    [Fact]
    public void MaterialGraphicPathScan_ReturnsExplicitAtlasPath()
    {
        using MemoryStream stream = new();
        WritePath("world/jd5/test/graph/textures/", "sprite.png");
        WritePath("world/jd5/test/graph/atlases/", "sprite_custom.atl");
        WritePath("world/jd5/test/graph/materials/", "sprite.msh");
        stream.Write(new byte[CinematicConstants.MaterialGraphicTailLength]);
        byte[] bytes = stream.ToArray();
        CinematicBinaryReader reader = new(bytes);

        bool found = CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
            reader,
            searchStartOffset: 0,
            searchLength: bytes.Length,
            out string? materialPath,
            out IReadOnlyList<string> texturePaths,
            out string? explicitAtlasPath,
            out int atlasTextureSlot,
            out int materialTailOffset,
            out int componentEndOffset);

        Assert.True(found);
        Assert.Equal(0, atlasTextureSlot);
        Assert.Equal("world/jd5/test/graph/textures/sprite.png", Assert.Single(texturePaths));
        Assert.Equal("world/jd5/test/graph/atlases/sprite_custom.atl", explicitAtlasPath);
        Assert.Equal("world/jd5/test/graph/materials/sprite.msh", materialPath);
        Assert.Equal(bytes.Length - CinematicConstants.MaterialGraphicTailLength, materialTailOffset);
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
    public void MaterialGraphicPathScan_ReturnsTextureOnlyActorWithoutMaterial()
    {
        using MemoryStream stream = new();
        WritePath("world/jd5/test/graph/textures/", "black.png");
        byte[] bytes = stream.ToArray();
        CinematicBinaryReader reader = new(bytes);

        bool found = CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
            reader,
            searchStartOffset: 0,
            searchLength: bytes.Length,
            out string? materialPath,
            out IReadOnlyList<string> texturePaths,
            out string? explicitAtlasPath,
            out int atlasTextureSlot,
            out int materialTailOffset,
            out int componentEndOffset);

        Assert.True(found);
        Assert.Equal(0, atlasTextureSlot);
        Assert.Equal("world/jd5/test/graph/textures/black.png", Assert.Single(texturePaths));
        Assert.Null(materialPath);
        Assert.Null(explicitAtlasPath);
        Assert.Equal(0, materialTailOffset);
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
    public void MaterialGraphicPathScan_ReturnsMaterialWhenTailBoundaryIsNotSerialized()
    {
        using MemoryStream stream = new();
        WritePath("world/jd5/ymca/graph/textures/", "x_dot_color_512x512.tga");
        WritePath("world/jd5/ymca/graph/textures/", "x_dot_512x512.tga");
        WritePath("world/jd5/ymca/graph/materials/", "x_dot_512x512.msh");
        byte[] bytes = stream.ToArray();
        CinematicBinaryReader reader = new(bytes);

        bool found = CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
            reader,
            searchStartOffset: 0,
            searchLength: bytes.Length,
            out string? materialPath,
            out IReadOnlyList<string> texturePaths,
            out string? explicitAtlasPath,
            out int atlasTextureSlot,
            out int materialTailOffset,
            out int componentEndOffset);

        Assert.True(found);
        Assert.Equal(0, atlasTextureSlot);
        Assert.Equal(
            [
                "world/jd5/ymca/graph/textures/x_dot_color_512x512.tga",
                "world/jd5/ymca/graph/textures/x_dot_512x512.tga"
            ],
            texturePaths);
        Assert.Equal("world/jd5/ymca/graph/materials/x_dot_512x512.msh", materialPath);
        Assert.Null(explicitAtlasPath);
        Assert.Equal(0, materialTailOffset);
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
    public void MaterialGraphicTextureOrder_PreservesSerializedSlotsWhenMaterialNameMatchesLaterLayer()
    {
        IReadOnlyList<string> ordered = CinematicTemplateVisualResolver.NormalizeTextureOrderForMaterial(
            [
                "world/jd5/ymca/graph/textures/x_dot_color_512x512.tga",
                "world/jd5/ymca/graph/textures/x_dot_512x512.tga"
            ]);

        Assert.Equal(
            [
                "world/jd5/ymca/graph/textures/x_dot_color_512x512.tga",
                "world/jd5/ymca/graph/textures/x_dot_512x512.tga"
            ],
            ordered);
    }

    [Fact]
    public void MaterialGraphicTextureOrder_PreservesNumberedVariantsBeforePrimary()
    {
        IReadOnlyList<string> ordered = CinematicTemplateVisualResolver.NormalizeTextureOrderForMaterial(
            [
                "world/jd5/gigolo/graph/textures/circle_mask02.tga",
                "world/jd5/gigolo/graph/textures/circle_mask.tga",
                "world/jd5/gigolo/graph/textures/circle_mask01.tga"
            ]);

        Assert.Equal(
            [
                "world/jd5/gigolo/graph/textures/circle_mask02.tga",
                "world/jd5/gigolo/graph/textures/circle_mask.tga",
                "world/jd5/gigolo/graph/textures/circle_mask01.tga"
            ],
            ordered);
    }

    [Fact]
    public void MaterialGraphicPathScan_StopsAtNextActorBoundary()
    {
        using MemoryStream stream = new();
        stream.Write(new byte[16]);
        Span<byte> actorType = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(
            actorType,
            LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>());
        stream.Write(actorType);
        WritePath("world/jd5/test/graph/textures/", "borrowed.png");
        WritePath("world/jd5/test/graph/materials/", "borrowed.msh");
        stream.Write(new byte[CinematicConstants.MaterialGraphicTailLength]);
        byte[] bytes = stream.ToArray();
        CinematicBinaryReader reader = new(bytes);

        bool found = CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
            reader,
            searchStartOffset: 0,
            searchLength: bytes.Length,
            out _,
            out IReadOnlyList<string> texturePaths,
            out _,
            out _,
            out _,
            out _);

        Assert.False(found);
        Assert.Empty(texturePaths);

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
    public void MaterialGraphicLayout_Jd2014ReadsAtlasIndexBeforeAnchor()
    {
        byte[] bytes = new byte[128];
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAtlasIndexOffset, -1);
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAnchorOffset, (int)TextureAnchor.Custom);
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorXOffset, -0.25f);
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorYOffset, 0.5f);

        int rawAtlasIndex = CinematicSceneBoundaryReader.ReadInt32OrDefault(
            bytes,
            CinematicConstants.MaterialGraphicAtlasIndexOffset);
        int atlasIndex = CinematicSceneBoundaryReader.ReadMaterialGraphicAtlasIndexOrDefault(
            bytes,
            CinematicConstants.MaterialGraphicAtlasIndexOffset);
        TextureAnchor anchor = CinematicSceneBoundaryReader.ReadTextureAnchorOrDefault(
            bytes,
            CinematicConstants.MaterialGraphicAnchorOffset);
        CinematicMaterialGraphicFields fields = CinematicSceneBoundaryReader.ReadMaterialGraphicFields(
            bytes,
            0,
            new LegacyBinarySerializerContext(2014));

        Assert.Equal(-1, rawAtlasIndex);
        Assert.Equal(0, atlasIndex);
        Assert.Equal(TextureAnchor.Custom, anchor);
        Assert.Equal(0, fields.AtlasIndex);
        Assert.Equal(TextureAnchor.Custom, fields.Anchor);
        AssertClose(-0.25, fields.CustomAnchorX);
        AssertClose(0.5, fields.CustomAnchorY);
        AssertClose(-0.25, CinematicSceneBoundaryReader.ReadSingleOrDefault(
            bytes,
            CinematicConstants.MaterialGraphicCustomAnchorXOffset,
            0));
        AssertClose(0.5, CinematicSceneBoundaryReader.ReadSingleOrDefault(
            bytes,
            CinematicConstants.MaterialGraphicCustomAnchorYOffset,
            0));
    }

    [Fact]
    public void MaterialGraphicLayout_Jd2015ReadsCompactAnchorFields()
    {
        byte[] bytes = new byte[128];
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAtlasIndexOffset, 0x11223344);
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAnchorOffset, unchecked((int)0x41424344));
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorXOffset, 9876.0f);
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorYOffset, -9876.0f);
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAtlasIndexOffset2015, 0);
        WriteInt32(bytes, CinematicConstants.MaterialGraphicAnchorOffset2015, (int)TextureAnchor.Custom);
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorXOffset2015, -0.3f);
        WriteSingle(bytes, CinematicConstants.MaterialGraphicCustomAnchorYOffset2015, -0.7f);

        CinematicMaterialGraphicFields fields = CinematicSceneBoundaryReader.ReadMaterialGraphicFields(
            bytes,
            0,
            new LegacyBinarySerializerContext(2015));

        Assert.Equal(0, fields.AtlasIndex);
        Assert.Equal(TextureAnchor.Custom, fields.Anchor);
        AssertClose(-0.3, fields.CustomAnchorX);
        AssertClose(-0.7, fields.CustomAnchorY);
    }
}