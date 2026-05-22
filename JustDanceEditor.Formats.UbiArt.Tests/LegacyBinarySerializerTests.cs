using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class LegacyBinarySerializerTests
{
    [Fact]
    public void Deserialize_UsesBaseToDerivedDeclarationOrder()
    {
        byte[] bytes = CreateBytes(
            1u,
            2u,
            3u);

        DerivedOrderModel model = LegacyBinarySerializer.Deserialize<DerivedOrderModel>(bytes);
        byte[] serialized = LegacyBinarySerializer.Serialize(model);

        Assert.Equal(1, model.BaseValue);
        Assert.Equal(2, model.FirstDerivedValue);
        Assert.Equal(3, model.SecondDerivedValue);
        Assert.Equal(bytes, serialized);
    }

    [Fact]
    public void Deserialize_ValidatesConcreteTypeIdAttribute()
    {
        byte[] bytes = CreateBytes(
            0xAABBCCDDu,
            0x12345678u);

        TypedValueModel model = LegacyBinarySerializer.Deserialize<TypedValueModel>(bytes);
        byte[] serialized = LegacyBinarySerializer.Serialize(model);

        Assert.Equal(0x12345678u, model.Value);
        Assert.Equal(bytes, serialized);
    }

    [Fact]
    public void DeserializeTyped_DispatchesCinematicClipTypeId()
    {
        byte[] bytes = CreateBytes(
            LegacyBinarySerializer.GetTypeId<LegacyCinematicPositionClipBinary>(),
            0x40u,
            10u,
            20u,
            1u,
            30u,
            40u);
        LegacyBinaryBufferReader reader = new(bytes);

        LegacyCinematicTapeClipBinary clip = LegacyBinarySerializer.DeserializeTyped<LegacyCinematicTapeClipBinary>(reader);

        LegacyCinematicPositionClipBinary positionClip = Assert.IsType<LegacyCinematicPositionClipBinary>(clip);
        Assert.Equal(0x40, positionClip.SerializedSize);
        Assert.Equal(10u, positionClip.Id);
        Assert.Equal(20u, positionClip.TrackId);
        Assert.Equal(1, positionClip.IsActive);
        Assert.Equal(30, positionClip.StartFrame);
        Assert.Equal(40, positionClip.DurationFrames);
        Assert.Equal(bytes.Length, reader.Offset);
    }

    [Fact]
    public void Deserialize_ReadsCinematicPickableInClassOrder()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, LegacyBinarySerializer.GetTypeId<LegacyCinematicSceneActorBinary>());
        WriteSingle(stream, 3.5f);
        WriteSingle(stream, 2.0f);
        WriteSingle(stream, 4.0f);
        WriteUInt32(stream, 1);
        WriteString(stream, "actor");
        WriteUInt32(stream, 1);
        WriteSingle(stream, 11.0f);
        WriteSingle(stream, 12.0f);
        WriteSingle(stream, 0.5f);
        WritePathFileFirst(stream, "unused.tpl", "world/maps/test/", 0);
        WriteUInt32(stream, 0);
        WritePathFileFirst(stream, "template.tpl", "world/maps/test/", 0x11223344);

        LegacyCinematicSceneActorBinary actor = LegacyBinarySerializer.Deserialize<LegacyCinematicSceneActorBinary>(stream.ToArray());
        LegacyCinematicPickableFields pickable = actor.Pickable.ToRuntime();

        Assert.Equal("actor", pickable.Name);
        Assert.Equal(3.5f, pickable.RelativeZ);
        Assert.Equal(2.0f, pickable.ScaleX);
        Assert.Equal(4.0f, pickable.ScaleY);
        Assert.Equal(1u, pickable.XFlipped);
        Assert.Equal(11.0f, pickable.PositionX);
        Assert.Equal(12.0f, pickable.PositionY);
        Assert.Equal(0.5f, pickable.Angle);
        Assert.Equal("world/maps/test/template.tpl", pickable.TemplatePath);
    }

    [Fact]
    public void ReadActorTrailer_ReadsSerializedObjectPathVersion2()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 1);
        WriteString(stream, "Plaques_ALL");
        WriteUInt32(stream, 0);
        WriteString(stream, "Background_Ceiling_Plane01");
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteSingle(stream, -0.031341f);
        WriteSingle(stream, 0.169997f);
        WriteSingle(stream, 0.000435f);
        WriteSingle(stream, 0.0f);
        WriteSingle(stream, 1.015f);
        WriteSingle(stream, 1.15f);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 123);

        LegacyCinematicActorTrailer trailer = LegacyCinematicSceneActorReader.ReadActorTrailer(new LegacyCinematicBinaryReader(stream.ToArray()));

        Assert.NotNull(trailer.ParentBind);
        Assert.Equal(123, trailer.ComponentVersion);
        Assert.Equal("Background_Ceiling_Plane01", trailer.ParentBind.ParentActorName);
        Assert.Equal(-0.031341f, trailer.ParentBind.OffsetX, 6);
        Assert.Equal(0.169997f, trailer.ParentBind.OffsetY, 6);
        Assert.Equal(1.015f, trailer.ParentBind.LocalScaleX, 6);
        Assert.Equal(1.15f, trailer.ParentBind.LocalScaleY, 6);
        Assert.Equal(1u, trailer.ParentBind.UseParentFlip);
        Assert.Equal(2, trailer.ParentBind.ScaleInheritProp);
    }

    private class BaseOrderModel
    {
        public int BaseValue { get; set; }
    }

    private sealed class DerivedOrderModel : BaseOrderModel
    {
        public int FirstDerivedValue { get; set; }

        public int SecondDerivedValue { get; set; }
    }

    [LegacyBinaryTypeId(0xAABBCCDD)]
    private sealed class TypedValueModel
    {
        public uint Value { get; set; }
    }

    private static byte[] CreateBytes(params uint[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(uint)];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)), values[i]);

        return bytes;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteSingle(Stream stream, float value) =>
        WriteUInt32(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    private static void WritePathFolderFirst(Stream stream, string folder, string fileName, uint resourceId)
    {
        WriteString(stream, folder);
        WriteString(stream, fileName);
        WriteUInt32(stream, resourceId);
    }

    private static void WritePathFileFirst(Stream stream, string fileName, string folder, uint resourceId)
    {
        WriteString(stream, fileName);
        WriteString(stream, folder);
        WriteUInt32(stream, resourceId);
    }
}