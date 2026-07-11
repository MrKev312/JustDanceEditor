using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicBinaryReaderTests
{
    [Fact]
    public void BinaryReader_TargetDescriptorWithSpawnedActorNameReadsSegment()
    {
        const string spawnedActorName = "x_Bubbles_256x256";
        byte[] nameBytes = Encoding.UTF8.GetBytes(spawnedActorName);
        byte[] bytes = new byte[16 + nameBytes.Length];
        WriteInt32(bytes, 0, 0x30);
        WriteInt32(bytes, 4, 0);
        WriteInt32(bytes, 8, nameBytes.Length);
        nameBytes.CopyTo(bytes.AsSpan(12));
        WriteInt32(bytes, 12 + nameBytes.Length, 0);

        CinematicBinaryReader reader = new(bytes);
        ActorTargetPath target = reader.ReadTargetDescriptor();

        Assert.Equal(spawnedActorName, Assert.Single(target.Segments));
        Assert.Equal(bytes.Length, reader.Offset);
    }

    [Fact]
    public void BinaryReader_JD2015TargetDescriptorReadsSegments()
    {
        byte[] bytes = CreateTargetDescriptorBytes(0x24, 0x10, "Speedy_GRAPH", "x_smoketray");

        CinematicBinaryReader reader = new(bytes);
        ActorTargetPath target = reader.ReadTargetDescriptor();

        Assert.Equal(["Speedy_GRAPH", "x_smoketray"], target.Segments);
        Assert.Equal(bytes.Length, reader.Offset);
    }

    [Fact]
    public void BinaryReader_JD2015CompactObjectPathTargetReadsCameraSceneAndObject()
    {
        using MemoryStream stream = new();
        WriteInt(0x24);
        WriteInt(1);
        WriteInt(0x10);
        WriteString("_cameras.isc");
        WriteInt(0);
        WriteString("Camera_JD");
        WriteInt(1);

        CinematicBinaryReader reader = new(stream.ToArray());
        ActorTargetPath target = reader.ReadTargetDescriptor();

        Assert.Equal(["_cameras.isc", "Camera_JD"], target.Segments);
        Assert.Equal(stream.Length, reader.Offset);

        void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            stream.Write(bytes);
        }

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }

    [Fact]
    public void BinaryReader_JD2015CompactObjectPathTargetReadsInterleavedSegmentHeaders()
    {
        using MemoryStream stream = new();
        WriteInt(0x24);
        WriteInt(3);
        WriteInt(0x10);
        WriteString("_MashUp_GRAPH");
        WriteInt(0);
        WriteInt(0x10);
        WriteString("FX");
        WriteInt(0);
        WriteInt(0x10);
        WriteString("x_SpeedLines_00");
        WriteInt(0);
        WriteString("x_mashup_godrayscreen");
        WriteInt(0);

        CinematicBinaryReader reader = new(stream.ToArray());
        ActorTargetPath target = reader.ReadTargetDescriptor();

        Assert.Equal(["_MashUp_GRAPH", "FX", "x_SpeedLines_00", "x_mashup_godrayscreen"], target.Segments);
        Assert.Equal(stream.Length, reader.Offset);

        void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            stream.Write(bytes);
        }

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }

    [Fact]
    public void BinaryReader_MashupPaddedCompactObjectPathTargetReadsScopedFxActor()
    {
        using MemoryStream stream = new();
        WriteInt(0x30);
        WriteInt(3);
        WriteInt(0x18);
        WriteString("_MashUp_GRAPH");
        WriteInt(0);
        WriteInt(0x18);
        WriteString("FX");
        WriteInt(0);
        WriteInt(0x18);
        WriteString("x_LIGHT_Glow_Ball");
        WriteInt(0);
        WriteString("x_bokeh_lightblue");
        WriteInt(0);

        CinematicBinaryReader reader = new(stream.ToArray());
        ActorTargetPath target = reader.ReadTargetDescriptor();

        Assert.Equal(["_MashUp_GRAPH", "FX", "x_LIGHT_Glow_Ball", "x_bokeh_lightblue"], target.Segments);
        Assert.Equal(stream.Length, reader.Offset);

        void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            stream.Write(bytes);
        }

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }

    [Fact]
    public void BinaryReader_JD2014ParentBindReadsComponentContainerAfterFourFlags()
    {
        using MemoryStream stream = new();
        WriteActorTrailerPrefixWithParent("x_ikissed");
        WriteBindBody(removeWithParent: null);
        WriteInt(2);
        WriteUInt(0x8D4FFFB6);

        CinematicBinaryReader reader = new(stream.ToArray());
        CinematicActorTrailer trailer = CinematicSceneActorReader.ReadActorTrailer(
            reader,
            new LegacyBinarySerializerContext(2014));

        Assert.Equal("x_ikissed", trailer.ParentBind!.ParentActorName);
        Assert.Equal(2, trailer.ComponentVersion);
        Assert.Equal(0u, trailer.ParentBind.RemoveWithParent);
        Assert.Equal(0x8D4FFFB6u, reader.PeekUInt32());

        void WriteActorTrailerPrefixWithParent(string parentName)
        {
            WriteInt(0);
            WriteInt(1);
            WriteInt(0);
            WriteString(parentName);
            WriteInt(0);
            WriteInt(0);
            WriteInt(0);
        }

        void WriteBindBody(uint? removeWithParent)
        {
            WriteFloat(-0.25f);
            WriteFloat(0.5f);
            WriteFloat(0.125f);
            WriteFloat(0.0f);
            WriteFloat(1.0f);
            WriteFloat(1.0f);
            WriteUInt(1);
            WriteInt(2);
            WriteUInt(0);
            WriteUInt(0);
            if (removeWithParent is uint value)
                WriteUInt(value);
        }

        void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            stream.Write(bytes);
        }

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteUInt(uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteFloat(float value) =>
            WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    [Fact]
    public void BinaryReader_JD2015ParentBindConsumesRemoveWithParentBeforeComponents()
    {
        using MemoryStream stream = new();
        WriteActorTrailerPrefixWithParent("P03_Depth");
        WriteBindBody(removeWithParent: 1);
        WriteInt(1);
        WriteUInt(0x72B61FC5);

        CinematicBinaryReader reader = new(stream.ToArray());
        CinematicActorTrailer trailer = CinematicSceneActorReader.ReadActorTrailer(
            reader,
            new LegacyBinarySerializerContext(2015));

        Assert.Equal("P03_Depth", trailer.ParentBind!.ParentActorName);
        Assert.Equal(1, trailer.ComponentVersion);
        Assert.Equal(1u, trailer.ParentBind.RemoveWithParent);
        Assert.Equal(0x72B61FC5u, reader.PeekUInt32());

        void WriteActorTrailerPrefixWithParent(string parentName)
        {
            WriteInt(0);
            WriteInt(1);
            WriteInt(0);
            WriteString(parentName);
            WriteInt(0);
            WriteInt(0);
            WriteInt(0);
        }

        void WriteBindBody(uint? removeWithParent)
        {
            WriteFloat(-0.060445f);
            WriteFloat(-0.476619f);
            WriteFloat(0.143394f);
            WriteFloat(0.0f);
            WriteFloat(1.0f);
            WriteFloat(1.0f);
            WriteUInt(1);
            WriteInt(2);
            WriteUInt(0);
            WriteUInt(0);
            if (removeWithParent is uint value)
                WriteUInt(value);
        }

        void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            stream.Write(bytes);
        }

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteUInt(uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteFloat(float value) =>
            WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    [Fact]
    public void BinaryReader_JD2015CurvePrefixReadsBezierCurve()
    {
        using MemoryStream stream = new();
        WriteInt(4);
        WriteUInt(0xE2BC4FB2);
        WriteInt(0x14);
        WriteInt(1);
        WriteInt(0x18);
        WriteFloat(2);
        WriteFloat(3);
        WriteFloat(1);
        WriteFloat(2);
        WriteFloat(3);
        WriteFloat(4);

        CinematicBinaryReader reader = new(stream.ToArray());
        CinematicCurve curve = Assert.Single(reader.ReadCurveBlocks(1))!;
        CinematicKeyframe key = Assert.Single(curve.Keyframes);

        AssertClose(2, key.Time);
        AssertClose(3, key.Value);
        Assert.Equal(stream.Length, reader.Offset);

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteUInt(uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteFloat(float value) =>
            WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    [Fact]
    public void BinaryReader_JD2015FactoryCurvesReadEmptyConstantAndLinear()
    {
        using MemoryStream stream = new();
        WriteInt(4);
        WriteUInt(0xFFFFFFFF);
        WriteInt(4);
        WriteUInt(0xB7914191);
        WriteInt(0x08);
        WriteFloat(-0.5f);
        WriteInt(4);
        WriteUInt(0x4DE6D871);
        WriteInt(0x24);
        WriteFloat(1);
        WriteFloat(2);
        WriteFloat(1.5f);
        WriteFloat(2.5f);
        WriteFloat(10);
        WriteFloat(20);
        WriteFloat(9.5f);
        WriteFloat(19.5f);

        CinematicBinaryReader reader = new(stream.ToArray());
        IReadOnlyList<CinematicCurve?> curves = reader.ReadCurveBlocks(3);

        Assert.Null(curves[0]);
        CinematicKeyframe constant = Assert.Single(curves[1]!.Keyframes);
        AssertClose(0, constant.Time);
        AssertClose(-0.5, constant.Value);
        Assert.Equal(2, curves[2]!.Keyframes.Count);
        AssertClose(1, curves[2]!.Keyframes[0].Time);
        AssertClose(2, curves[2]!.Keyframes[0].Value);
        AssertClose(1.5, curves[2]!.Keyframes[0].RightHandleTime);
        AssertClose(2.5, curves[2]!.Keyframes[0].RightHandleValue);
        AssertClose(10, curves[2]!.Keyframes[1].Time);
        AssertClose(20, curves[2]!.Keyframes[1].Value);
        AssertClose(9.5, curves[2]!.Keyframes[1].LeftHandleTime);
        AssertClose(19.5, curves[2]!.Keyframes[1].LeftHandleValue);
        Assert.Equal(stream.Length, reader.Offset);

        void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteUInt(uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        void WriteFloat(float value) =>
            WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }
}