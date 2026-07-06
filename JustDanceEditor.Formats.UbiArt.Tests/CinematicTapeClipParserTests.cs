using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicTapeClipParserTests
{
    [Fact]
    public void TapeReader_JD2014SoundSetPathSkipsPaddingWord()
    {
        using MemoryStream stream = new();
        WriteInt(0);
        WritePath("amb_blurredlines_intro.tpl", "world/jd5/blurredlines/audio/amb/");

        CinematicBinaryReader reader = new(stream.ToArray());
        string soundSetPath = CinematicTapeClipParser.ReadSoundSetPath(
            reader,
            new LegacyBinarySerializerContext(2014));

        Assert.Equal("world/jd5/blurredlines/audio/amb/amb_blurredlines_intro.tpl", soundSetPath);
        Assert.Equal(stream.Length, reader.Offset);

        void WritePath(string fileName, string folder)
        {
            WriteString(fileName);
            WriteString(folder);
            WriteInt(0);
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
    }

    [Fact]
    public void TapeReader_JD2015SoundSetPathStartsImmediatelyAfterClipHeader()
    {
        using MemoryStream stream = new();
        WritePath("amb_speedy_intro.tpl", "world/maps/speedy/audio/amb/");

        CinematicBinaryReader reader = new(stream.ToArray());
        string soundSetPath = CinematicTapeClipParser.ReadSoundSetPath(
            reader,
            new LegacyBinarySerializerContext(2015));

        Assert.Equal("world/maps/speedy/audio/amb/amb_speedy_intro.tpl", soundSetPath);
        Assert.Equal(stream.Length, reader.Offset);

        void WritePath(string fileName, string folder)
        {
            WriteString(fileName);
            WriteString(folder);
            WriteInt(0);
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
    }
}