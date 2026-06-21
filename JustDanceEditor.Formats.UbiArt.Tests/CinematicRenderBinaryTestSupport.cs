using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Timeline;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

internal static class CinematicRenderBinaryTestSupport
{
    internal static byte[] CreateMaterialBytes(
        TextureAddressMode addressU,
        TextureAddressMode addressV,
        CinematicUvModifier modifier,
        bool enabled = true,
        int blendMode = 2,
        CinematicMaterialColor? diffuseColor = null,
        CinematicTextureUsage textureUsage = CinematicTextureUsage.EntireTexture,
        int uvModifierCount = 1,
        int layerSize = 0x38,
        bool? includeAlphaThreshold = null)
    {
        CinematicMaterialColor color = diffuseColor ?? CinematicMaterialColor.White;
        bool hasAlphaThreshold = includeAlphaThreshold ?? layerSize == 0x3C;
        int fixedLayerBytes = hasAlphaThreshold ? 0x2C : 0x28;
        byte[] bytes = new byte[0x6C + fixedLayerBytes + (Math.Max(uvModifierCount, 0) * 0x34)];
        WriteInt32(bytes, 0, 1);
        WriteInt32(bytes, 4, bytes.Length);
        WriteInt32(bytes, 0x64, blendMode);
        WriteInt32(bytes, 0x68, layerSize);
        int offset = 0x6C;
        WriteInt32(bytes, offset, enabled ? 1 : 0);
        offset += 4;
        if (hasAlphaThreshold)
        {
            WriteSingle(bytes, offset, 0.0f);
            offset += 4;
        }

        WriteInt32(bytes, offset, (int)addressU);
        offset += 4;
        WriteInt32(bytes, offset, (int)addressV);
        offset += 4;
        WriteInt32(bytes, offset, 1);
        offset += 4;
        WriteSingle(bytes, offset, (float)color.Blue);
        offset += 4;
        WriteSingle(bytes, offset, (float)color.Green);
        offset += 4;
        WriteSingle(bytes, offset, (float)color.Red);
        offset += 4;
        WriteSingle(bytes, offset, (float)color.Alpha);
        offset += 4;
        WriteInt32(bytes, offset, (int)textureUsage);
        offset += 4;
        WriteInt32(bytes, offset, uvModifierCount);
        offset += 4;
        for (int index = 0; index < uvModifierCount; index++)
        {
            WriteInt32(bytes, offset, 0x30);
            WriteModifier(bytes, offset + 4, modifier);
            offset += 0x34;
        }

        return bytes;
    }


    internal static byte[] CreateParticleTemplateBytes()
    {
        byte[] bytes = new byte[0x280];
        int offset = 0x40;

        void WriteUInt(uint value)
        {
            WriteUInt32(bytes, offset, value);
            offset += 4;
        }

        void WriteInt(int value)
        {
            WriteInt32(bytes, offset, value);
            offset += 4;
        }

        void WriteFloat(float value)
        {
            WriteSingle(bytes, offset, value);
            offset += 4;
        }

        void WriteBool(bool value) => WriteUInt(value ? 1u : 0u);
        void WriteVec2(float x, float y)
        {
            WriteFloat(x);
            WriteFloat(y);
        }

        void WriteVec3(float x, float y, float z)
        {
            WriteFloat(x);
            WriteFloat(y);
            WriteFloat(z);
        }

        void WriteColor(float red, float green, float blue, float alpha)
        {
            WriteFloat(blue);
            WriteFloat(green);
            WriteFloat(red);
            WriteFloat(alpha);
        }

        void WriteBox(float minX, float minY, float maxX, float maxY)
        {
            WriteInt(0x10);
            WriteVec2(minX, minY);
            WriteVec2(maxX, maxY);
        }

        void WritePhase(float phaseTime, float alpha, int animEnd)
        {
            WriteInt(0x54);
            WriteFloat(phaseTime);
            WriteColor(1, 1, 1, alpha);
            WriteColor(1, 1, 1, alpha);
            WriteVec2(0.5f, 0.5f);
            WriteVec2(0.5f, 0.5f);
            WriteInt(0);
            WriteInt(animEnd);
            WriteUInt(uint.MaxValue);
            WriteFloat(0);
            WriteBool(false);
            WriteBool(true);
        }

        WriteUInt(5);
        WriteColor(1, 1, 1, 1);
        WriteUInt(uint.MaxValue);
        WriteBool(false);
        WriteBool(true);
        WriteFloat(-1);
        WriteFloat(-1);
        WriteVec3(0, 0, 0);
        WriteVec2(0, 0);
        WriteFloat(0);
        WriteFloat(-90);
        WriteFloat(0);
        WriteVec3(0, -3, 0);
        WriteVec3(0, 1, 0);
        WriteFloat(0);
        WriteBool(true);
        WriteFloat(2);
        WriteFloat(1);
        WriteFloat(0.05f);
        WriteFloat(0);
        WriteBool(false);
        WriteUInt(1);
        WriteUInt(1);
        WriteUInt(uint.MaxValue);
        WriteFloat(0);
        WriteFloat(MathF.PI);
        WriteFloat(6.981317f);
        WriteFloat(5.235988f);
        WriteFloat(0);
        WriteUInt(2);
        WriteFloat(0.25f);
        WriteFloat(0);
        WriteFloat(4);
        WriteFloat(5);
        WriteVec3(1, 1, 1);
        WriteVec3(0, 0, 0);
        WriteBool(true);
        WriteUInt(1);
        WriteBool(false);
        WriteBox(-1, 0, 1, 0);
        WriteFloat(0);
        WriteUInt(0);
        WriteFloat(0);
        WriteFloat(1);
        WriteFloat(-1);
        WriteBox(-10, -20, 10, 20);
        WriteUInt(0);
        WriteUInt(0);
        WriteUInt(0);
        WriteFloat(0.7f);
        WriteBool(false);
        WriteBool(false);
        WriteUInt(128);
        WriteUInt(128);
        WriteFloat(-MathF.PI);
        WriteFloat(MathF.PI);
        WriteBool(true);
        WriteBool(true);
        WriteBool(true);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteUInt(10);
        WriteFloat(0);
        WriteUInt(1);
        WriteBool(true);
        WriteBool(true);
        WriteVec2(0, 0);
        WriteBool(false);
        WriteUInt(2);
        WritePhase(0.3f, 0, 35);
        WritePhase(2.75f, 1, 24);

        return bytes;
    }


    internal static byte[] CreateAtlasContainerWithoutUvParametersBytes(
        string firstTexturePath,
        string secondTexturePath)
    {
        using MemoryStream stream = new();

        WriteUInt(0);
        WriteInt(2);
        WriteAtlas(
            CinematicStringId.Compute(firstTexturePath),
            64,
            64,
            [(0, 0), (1, 0), (1, 1), (0, 1)]);
        WriteAtlas(
            CinematicStringId.Compute(secondTexturePath),
            128,
            32,
            [(0, 0), (1, 1)]);

        return stream.ToArray();

        void WriteAtlas(uint atlasId, float width, float height, (float X, float Y)[] uvs)
        {
            WriteUInt(atlasId);
            WriteInt(18);
            WriteFloat(width);
            WriteFloat(height);
            WriteInt(1);
            WriteInt(0);
            WriteInt(uvs.Length);
            foreach ((float x, float y) in uvs)
            {
                WriteFloat(x);
                WriteFloat(y);
            }
        }

        void WriteInt(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        void WriteUInt(uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        void WriteFloat(float value) =>
            WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }


    internal static void WriteModifier(byte[] bytes, int offset, CinematicUvModifier modifier)
    {
        WriteSingle(bytes, offset, modifier.TranslationU);
        WriteSingle(bytes, offset + 4, modifier.TranslationV);
        WriteInt32(bytes, offset + 8, modifier.AnimTranslationU ? 1 : 0);
        WriteInt32(bytes, offset + 12, modifier.AnimTranslationV ? 1 : 0);
        WriteSingle(bytes, offset + 16, modifier.Rotation);
        WriteSingle(bytes, offset + 20, modifier.RotationOffsetU);
        WriteSingle(bytes, offset + 24, modifier.RotationOffsetV);
        WriteInt32(bytes, offset + 28, modifier.AnimRotation ? 1 : 0);
        WriteSingle(bytes, offset + 32, modifier.ScaleU);
        WriteSingle(bytes, offset + 36, modifier.ScaleV);
        WriteSingle(bytes, offset + 40, modifier.ScaleOffsetU);
        WriteSingle(bytes, offset + 44, modifier.ScaleOffsetV);
    }


    internal static CinematicActor CreateActor(params string[] path) =>
        new(
            path,
            path[^1],
            SourceOffset: 0,
            SiblingOrder: 0,
            TypeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            RelativeZ: 0,
            ScaleX: 1,
            ScaleY: 1,
            XFlipped: 0,
            Angle: 0,
            PositionX: 0,
            PositionY: 0,
            TemplatePath: string.Empty,
            SubScenePath: null,
            TexturePath: null,
            TexturePaths: [],
            MaterialPath: null,
            ExplicitAtlasPath: null,
            MeshPath: null,
            VisualComponentTypeId: null,
            AtlasIndex: 0,
            AtlasTextureSlot: 0,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0,
            CustomAnchorY: 0,
            ScenePriority: 0,
            DefaultEnabled: true,
            BaseTint: RgbTint.White,
            BaseAlpha: 1,
            ParentBind: null);


    internal static ResolvedActorState CreateState(float x = 0, float y = 0, float z = 0) =>
        new(
            x,
            y,
            z,
            ScaleX: 1,
            ScaleY: 1,
            Angle: 0,
            Alpha: 1,
            RgbTint.White,
            XFlipped: false);


    internal static byte[] CreateTargetDescriptorBytes(int tableSize, int segmentHeaderSize, params string[] segments)
    {
        using MemoryStream stream = new();
        WriteInt(tableSize);
        WriteInt(segments.Length);
        WriteInt(segmentHeaderSize);
        WriteInt(0);
        WriteInt(1);
        WriteInt(segmentHeaderSize);

        for (int i = 0; i < segments.Length; i++)
        {
            byte[] segmentBytes = Encoding.UTF8.GetBytes(segments[i]);
            WriteInt(segmentBytes.Length);
            stream.Write(segmentBytes);
            WriteInt(0);
            if (i < segments.Length - 2)
                WriteInt(0);
        }

        return stream.ToArray();

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }


    internal static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), value);


    internal static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);


    internal static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(offset, 4),
            unchecked((uint)BitConverter.SingleToInt32Bits(value)));


    internal static void AssertClose(double expected, double actual, double tolerance = 0.0001) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:0.####}, got {actual:0.####}.");
}
