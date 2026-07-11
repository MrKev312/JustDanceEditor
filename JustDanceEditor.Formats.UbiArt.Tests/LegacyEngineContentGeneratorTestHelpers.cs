using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

internal static class LegacyEngineContentGeneratorTestHelpers
{
    internal static IntermediateSongPackage CreatePackage() => new()
    {
        Metadata = new IntermediateMetadata
        {
            MapName = "TestMap",
            Artist = "Artist",
            Title = "Title",
            OriginalJDVersion = 2016,
            CoachCount = 1,
            Difficulty = 2,
            LyricsColor = "#11223344"
        },
        TimelineStructure = new TimelineStructureDocument
        {
            Markers = [0, 24000, 48000],
            Signatures = [new SignatureSegment { Marker = 0, Beats = 4 }],
            Sections = [new SectionSegment { StartBeat = 0, SectionType = SongSectionType.Verse, Comment = "verse" }],
            StartBeat = -1,
            EndBeat = 2,
            PreviewEntryBeat = 0,
            PreviewLoopStartBeat = 1,
            PreviewLoopEndBeat = 2
        },
        CoachTimelines =
        [
            new MoveTimeline
        {
            CoachId = 0,
            Clips =
            [
                new MoveClip { Id = 1, StartTime = 10, MoveId = "move_a", IsGoldMove = true }
            ]
        }
        ],
        HandCoachMoves =
    {
        ["move_a"] = new CoachMoveDefinition { Duration = 24, Color = "#11223344" }
    },
        Pictograms = new Timeline<PictogramClip>
        {
            Clips =
            [
                new PictogramClip { Id = 2, StartTime = 20, Duration = 12, PictogramId = "picto_a" }
            ]
        },
        GoldEffects = new Timeline<GoldEffectClip>
        {
            Clips =
            [
                new GoldEffectClip { Id = 3, StartTime = 30, Duration = 8, EffectType = 1 }
            ]
        },
        Lyrics = new Timeline<KaraokeClip>
        {
            Clips =
            [
                new KaraokeClip { Id = 5, StartTime = 50, Duration = 20, Lyrics = "Test", Pitch = 8.175798f, ContentType = 1 }
            ]
        },
        HideUserInterface = new Timeline<HideUserInterfaceClip>
        {
            Clips =
            [
                new HideUserInterfaceClip { Id = 4, StartTime = 40, Duration = 16, IsActive = true }
            ]
        }
    };

    internal static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));

    internal static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    internal static void WriteSingle(Stream stream, float value) =>
        WriteUInt32(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    internal static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    internal static byte[] CreateOfficialJd2014SongDescWithRelatedAlbumsAndColoredEntries()
    {
        using MemoryStream stream = new();

        WriteResourceHeader(stream, 0x8AC2B5C6, componentSize: 0x104, baseTypeSize: 0xAC);
        WriteString(stream, "VsMap");
        WriteUInt32(stream, 5);
        WriteUInt32(stream, 2);
        WriteString(stream, "ParentA");
        WriteString(stream, "ParentB");

        WriteUInt32(stream, 3);
        WriteJd2014SongDescEntry(stream, group: 0, value: 0, category: 3, hasColor: false, enabled: true);
        WriteJd2014SongDescEntry(stream, group: 1, value: 0, category: 2, hasColor: true, enabled: false);
        WriteJd2014SongDescEntry(stream, group: 2, value: 0, category: 2, hasColor: false, enabled: true);

        WriteString(stream, "Artist With Parents");
        WriteString(stream, "Title With Colored Entry");
        WriteUInt32(stream, 4);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 3);
        WriteSingle(stream, 0.5f);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 0x10);
        WriteUInt32(stream, 0x6F4037D0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0x10);
        WriteUInt32(stream, 0xB11FC1B6);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 0x31D3B347);
        WriteSingle(stream, 1.0f);
        WriteSingle(stream, 0.2f);
        WriteSingle(stream, 0.4f);
        WriteSingle(stream, 0.6f);

        return stream.ToArray();
    }

    internal static void WriteResourceHeader(Stream stream, uint componentTypeId, uint componentSize, uint baseTypeSize)
    {
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0x1B857BCE);
        WriteUInt32(stream, baseTypeSize);
        for (int i = 0; i < 7; i++)
            WriteUInt32(stream, 0);

        WriteUInt32(stream, 1);
        WriteUInt32(stream, componentTypeId);
        WriteUInt32(stream, componentSize);
    }

    internal static void WriteJd2014SongDescEntry(Stream stream, uint group, uint value, uint category, bool hasColor, bool enabled)
    {
        WriteUInt32(stream, 0x9C);
        WriteUInt32(stream, group);
        WriteUInt32(stream, value);
        WriteUInt32(stream, category);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, hasColor ? 1u : 0u);
        if (hasColor)
        {
            WriteUInt32(stream, 0x31D3B347);
            WriteSingle(stream, 1.0f);
            WriteSingle(stream, 0.0f);
            WriteSingle(stream, 1.0f);
            WriteSingle(stream, 1.0f);
        }

        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, uint.MaxValue);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, enabled ? 1u : 0u);
    }

    internal static bool ContainsUInt32(byte[] bytes, uint value)
    {
        return IndexOfUInt32(bytes, value) >= 0;
    }

    internal static int IndexOfUInt32(byte[] bytes, uint value)
    {
        for (int i = 0; i <= bytes.Length - sizeof(uint); i++)
        {
            if (ReadUInt32(bytes, i) == value)
                return i;
        }

        return -1;
    }

    internal static int IndexOfAscii(byte[] bytes, string value)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(value);
        for (int i = 0; i <= bytes.Length - pattern.Length; i++)
        {
            bool matches = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (bytes[i + j] != pattern[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return i;
        }

        return -1;
    }

    internal static int FindPathTailOffset(byte[] bytes, string fileName, string folder)
    {
        int fileOffset = IndexOfAscii(bytes, fileName);
        Assert.True(fileOffset >= sizeof(uint));
        Assert.Equal((uint)fileName.Length, ReadUInt32(bytes, fileOffset - sizeof(uint)));

        int folderLengthOffset = fileOffset + fileName.Length;
        Assert.Equal((uint)folder.Length, ReadUInt32(bytes, folderLengthOffset));
        int folderOffset = folderLengthOffset + sizeof(uint);
        AssertAsciiAt(bytes, folderOffset, folder);

        return folderOffset + folder.Length + sizeof(uint);
    }

    internal static void AssertAsciiAt(byte[] bytes, int offset, string value)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(value);
        for (int i = 0; i < pattern.Length; i++)
            Assert.Equal(pattern[i], bytes[offset + i]);
    }

    internal static string ReadAscii(byte[] bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] is >= 32 and <= 126 ? (char)bytes[i] : '.';

        return new string(chars);
    }

    internal static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JustDanceEditor.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}