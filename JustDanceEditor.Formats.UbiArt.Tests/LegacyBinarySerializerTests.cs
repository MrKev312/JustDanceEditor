using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;

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
    public void Deserialize_SkippedEngineVersionMemberPreservesModelDefault()
    {
        EngineConditionDefaultModel model = LegacyBinarySerializer.Deserialize<EngineConditionDefaultModel>(
            CreateBytes(0x12345678u),
            new LegacyBinarySerializerContext(2015));

        Assert.Equal(7u, model.Jd2014OnlyValue);
        Assert.Equal(0x12345678u, model.AlwaysValue);
    }

    [Fact]
    public void Deserialize_EngineVersionMemberReadsWhenIncluded()
    {
        EngineConditionDefaultModel model = LegacyBinarySerializer.Deserialize<EngineConditionDefaultModel>(
            CreateBytes(0x11111111u, 0x22222222u),
            new LegacyBinarySerializerContext(2014));

        Assert.Equal(0x11111111u, model.Jd2014OnlyValue);
        Assert.Equal(0x22222222u, model.AlwaysValue);
    }

    [Fact]
    public void Deserialize_Jd2014BetaSongDesc_UsesCompactMetadataLayout()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0x15B);
        WriteUInt32(stream, 0x1B857BCE);
        WriteUInt32(stream, 0xAC);
        stream.Write(new byte[28]);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0x8AC2B5C6);
        WriteUInt32(stream, 0x88);
        WriteString(stream, "BlameIt");
        WriteUInt32(stream, 5);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0);
        WriteString(stream, "TBD");
        WriteString(stream, "Blame It On The Boogie");
        WriteUInt32(stream, 4);
        WriteUInt32(stream, 2);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteSingle(stream, 16.0f);

        LegacySongDesc legacy = LegacyBinarySerializer.Deserialize<LegacySongDesc>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2014));
        SongDesc songDesc = (SongDesc)legacy;
        InfoComponent info = Assert.Single(songDesc.Components);

        Assert.Equal("BlameIt", info.MapName);
        Assert.Equal("TBD", info.Artist);
        Assert.Equal("Blame It On The Boogie", info.Title);
        Assert.Equal(4, info.NumCoach);
        Assert.Equal(2u, info.Difficulty);
        Assert.Equal(0, info.MainCoach);
    }

    [Fact]
    public void Deserialize_Jd2015BaseMapSongDesc_ReadsPopulatedPreview()
    {
        LegacyModernSongDescComponent source = new()
        {
            MapName = "DancingQueenCMU",
            RawEngineVersion = 2015,
            OriginalVersion = uint.MaxValue,
            HasBaseMap = 1,
            BaseMapName = "DancingQueen",
            Artist = "ABBA",
            Title = "Dancing Queen",
            CoachCount = 1,
            Preview = new LegacySongDescPreview
            {
                PreviewCount = 1,
                PreviewEntrySize = 0x14,
                PreviewEntryTypeId = 0x6F4037D0,
                PreviewEntryBeat = 48,
                PreviewLoopSize = 0x18,
                PreviewLoopTypeId = 0xEF70A59D,
                PreviewLoopStartBeat = 48,
                PreviewLoopEndBeat = 96
            }
        };
        byte[] bytes = LegacyBinarySerializer.Serialize(source);

        LegacyModernSongDescComponent parsed = LegacyBinarySerializer.Deserialize<LegacyModernSongDescComponent>(
            bytes,
            new LegacyBinarySerializerContext(2015));

        Assert.Equal("DancingQueen", parsed.BaseMapName);
        Assert.Equal("DancingQueen", Assert.Single(((SongDesc)parsed).Components).BaseMapName);
        Assert.Equal(1, parsed.Preview.PreviewCount);
        Assert.Equal(0x6F4037D0u, parsed.Preview.PreviewEntryTypeId);
        Assert.Equal(48, parsed.Preview.PreviewEntryBeat);
        Assert.Equal(96, parsed.Preview.PreviewLoopEndBeat);
    }

    [Fact]
    public void Deserialize_Jd2014BetaTimelineEvent_UsesInlineGeneratedEventBody()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, 0x48);
        WriteSingle(stream, 205.008148f);
        WriteSingle(stream, 206.008163f);
        WriteUInt32(stream, 6);
        WriteString(stream, "GoldMove");
        stream.Write(
        [
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x01,
            0x49, 0x69, 0x58, 0x69,
            0x00, 0x00, 0x00, 0x18
        ]);
        WriteString(stream, "GoldMove");

        LegacyJd2014TimelineEvent timelineEvent = LegacyBinarySerializer.Deserialize<LegacyJd2014TimelineEvent>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2014));
        Clip[] clips = (Clip[])timelineEvent;

        GoldEffectClip clip = Assert.IsType<GoldEffectClip>(Assert.Single(clips));
        Assert.Equal(1, clip.IsActive);
        Assert.Equal(1, clip.EffectType);
        Assert.True(clip.Duration > 0);
    }

    [Fact]
    public void DeserializeTyped_DispatchesCinematicClipTypeId()
    {
        byte[] bytes = CreateBytes(
            LegacyBinarySerializer.GetTypeId<CinematicPositionClipBinary>(),
            0x40u,
            10u,
            20u,
            1u,
            30u,
            40u);
        LegacyBinaryBufferReader reader = new(bytes);

        CinematicTapeClipBinary clip = LegacyBinarySerializer.DeserializeTyped<CinematicTapeClipBinary>(reader);

        CinematicPositionClipBinary positionClip = Assert.IsType<CinematicPositionClipBinary>(clip);
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
        WriteUInt32(stream, LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>());
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

        CinematicBinaryReader reader = new(stream.ToArray());
        Assert.True(LegacyBinarySerializer.IsTypeId<CinematicSceneActorBinary>(reader.ReadUInt32()));
        CinematicPickableFields pickable = CinematicPickableReader.Read(
            reader,
            new LegacyBinarySerializerContext(2014),
            hasTemplatePathPrefix: true);

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
    public void Deserialize_Jd2015CinematicPickable_ReadsTransformWithoutNewerEnableField()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, LegacyBinarySerializer.GetTypeId<CinematicSubSceneActorBinary>());
        WriteSingle(stream, 0.0f);
        WriteSingle(stream, 1.0f);
        WriteSingle(stream, 1.0f);
        WriteUInt32(stream, 0);
        WriteString(stream, "actor");
        WriteSingle(stream, -262.97263f);
        WriteSingle(stream, -2.323281f);
        WriteSingle(stream, 6.021383f);
        WritePathFileFirst(stream, "", "", 0);
        WriteUInt32(stream, 0);
        WritePathFileFirst(stream, "subscene.tpl", "enginedata/actortemplates/", 0x69934BE0);

        CinematicBinaryReader reader = new(stream.ToArray());
        Assert.True(LegacyBinarySerializer.IsTypeId<CinematicSubSceneActorBinary>(reader.ReadUInt32()));
        CinematicPickableFields pickable = CinematicPickableReader.Read(
            reader,
            new LegacyBinarySerializerContext(2015),
            hasTemplatePathPrefix: true);

        Assert.True(pickable.DefaultEnabled);
        Assert.Equal(-262.97263f, pickable.PositionX);
        Assert.Equal(-2.323281f, pickable.PositionY);
        Assert.Equal(6.021383f, pickable.Angle);
        Assert.Equal("enginedata/actortemplates/subscene.tpl", pickable.TemplatePath);
    }

    [Fact]
    public void Deserialize_Jd2015CommunityDancerClip_RetainsHudEventData()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, 0x0F95B841);
        WriteUInt32(stream, 0x34);
        WriteUInt32(stream, 12);
        WriteUInt32(stream, 34);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 120);
        WriteUInt32(stream, 100);
        WriteString(stream, "US");
        WriteUInt32(stream, 45);
        WriteString(stream, "Carl_Natassia");

        LegacyCommunityDancerGameplayClip legacy = LegacyBinarySerializer.Deserialize<LegacyCommunityDancerGameplayClip>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2015));
        CommunityDancerClip clip = (CommunityDancerClip)legacy;

        Assert.Equal(12, clip.Id);
        Assert.Equal(34, clip.TrackId);
        Assert.Equal(120, clip.StartTime);
        Assert.Equal(100, clip.Duration);
        Assert.Equal("US", clip.DancerCountryCode);
        Assert.Equal(45, clip.DancerAvatarId);
        Assert.Equal("Carl_Natassia", clip.DancerName);
    }

    [Fact]
    public void Deserialize_Jd2014BlockFlow_UsesDatabaseIdDescriptorLayout()
    {
        using MemoryStream stream = new();
        WriteBlockFlowHeader(stream, blockCount: 1);
        WriteUInt32(stream, 0x1A8);
        WriteJd2014BlockDescriptor(stream, "BlurredLines", 5, 7, 23, 0.0f, 0.0f, 1.0f);
        WriteUInt32(stream, 1);
        WriteJd2014BlockDescriptor(stream, "CrazyInLove_84", 2, 0, 16, 0.033854f, 0.0625f, 0.9376f);

        LegacyBlockFlow blockFlow = LegacyBinarySerializer.Deserialize<LegacyBlockFlow>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2014));
        LegacyMashupData mashup = blockFlow.ToMashupData("BlurredLinesMU", "BlurredLines", hasDatabaseGameId: true);

        Assert.Equal(1, blockFlow.Component.IsMashUp);
        LegacyMashupBlock block = Assert.Single(mashup.Blocks);
        Assert.Equal("BlurredLines", block.BaseBlock.SongName);
        Assert.Equal(5, block.BaseBlock.DatabaseGameId);
        Assert.Equal(7, block.BaseBlock.FirstBeat);
        Assert.Equal(23, block.BaseBlock.LastBeat);
        Assert.Equal("CrazyInLove_84", block.SourceBlock.SongName);
        Assert.Equal(2, block.SourceBlock.DatabaseGameId);
        Assert.Equal(16, block.DurationBeats);
    }

    [Fact]
    public void Deserialize_Jd2015BlockFlow_UsesSpeedAndGuidDescriptorLayout()
    {
        using MemoryStream stream = new();
        WriteBlockFlowHeader(stream, blockCount: 1);
        WriteUInt32(stream, 0xCC);
        WriteJd2015BlockDescriptor(stream, "TheFox", 3, 19, 0, 0.0f, 1.0f, isEmpty: false, "d20d15f2-d95a-48a7-8eb7-44002dd19dcd");
        WriteUInt32(stream, 1);
        WriteJd2015BlockDescriptor(stream, "JinGoLoBa_3", 0, 16, -0.083333f, -0.0703125f, 1.1128f, isEmpty: false, "066ab749-97e9-45cc-a0ad-7a918c11c9d0");

        LegacyBlockFlow blockFlow = LegacyBinarySerializer.Deserialize<LegacyBlockFlow>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2015));
        LegacyMashupData mashup = blockFlow.ToMashupData("TheFoxMU", "TheFox", hasDatabaseGameId: false);

        LegacyMashupBlock block = Assert.Single(mashup.Blocks);
        Assert.Null(block.BaseBlock.DatabaseGameId);
        Assert.Equal("TheFox", block.BaseBlock.SongName);
        Assert.Equal("JinGoLoBa_3", block.SourceBlock.SongName);
        Assert.Equal(-0.083333f, block.SourceBlock.VideoCoachOffsetX, 5);
        Assert.Equal("066ab749-97e9-45cc-a0ad-7a918c11c9d0", block.SourceBlock.Guid);
        Assert.Equal(16, block.DurationBeats);
    }

    [Fact]
    public void Deserialize_BlockFlow_UsesEngineSelectedBlockForMashupTiming()
    {
        using MemoryStream stream = new();
        WriteBlockFlowHeader(stream, blockCount: 2);

        WriteUInt32(stream, 0xCC);
        WriteJd2015BlockDescriptor(stream, "TheFox", 0, 4, 0.0f, 0.0f, 1.0f, isEmpty: true, "empty-base");
        WriteUInt32(stream, 1);
        WriteJd2015BlockDescriptor(stream, "JinGoLoBa_0", 0, 8, 0.0f, 0.0f, 1.0f, isEmpty: false, "ignored-alt");

        WriteUInt32(stream, 0xCC);
        WriteJd2015BlockDescriptor(stream, "TheFox", 4, 20, 0.0f, 0.0f, 1.0f, isEmpty: false, "base");
        WriteUInt32(stream, 1);
        WriteJd2015BlockDescriptor(stream, "JinGoLoBa_4", 0, 10, 0.0f, 0.0f, 1.0f, isEmpty: false, "selected-alt");

        LegacyBlockFlow blockFlow = LegacyBinarySerializer.Deserialize<LegacyBlockFlow>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2015));
        LegacyMashupData mashup = blockFlow.ToMashupData("TheFoxMU", "TheFox", hasDatabaseGameId: false);

        Assert.Equal(2, mashup.Blocks.Count);
        Assert.Equal("TheFox", mashup.Blocks[0].SourceBlock.SongName);
        Assert.True(mashup.Blocks[0].SourceBlock.IsEmptyBlock);
        Assert.False(mashup.Blocks[0].UsesAlternativeBlock);
        Assert.Equal(4, mashup.Blocks[0].DurationBeats);

        Assert.Equal(4, mashup.Blocks[1].AbsoluteStartBeat);
        Assert.Equal("JinGoLoBa_4", mashup.Blocks[1].SourceBlock.SongName);
        Assert.True(mashup.Blocks[1].UsesAlternativeBlock);
        Assert.Equal(10, mashup.Blocks[1].DurationBeats);
        Assert.Equal(14, mashup.DurationBeats);
    }

    [Fact]
    public void Deserialize_Jd2014BlockFlow_PreservesDatabaseIdZero()
    {
        using MemoryStream stream = new();
        WriteBlockFlowHeader(stream, blockCount: 1);
        WriteUInt32(stream, 0x1A8);
        WriteJd2014BlockDescriptor(stream, "DiscoBallMan2_298", 0, 0, 16, 0.0f, 0.0f, 1.0f);
        WriteUInt32(stream, 0);

        LegacyBlockFlow blockFlow = LegacyBinarySerializer.Deserialize<LegacyBlockFlow>(
            stream.ToArray(),
            new LegacyBinarySerializerContext(2014));
        LegacyMashupData mashup = blockFlow.ToMashupData("BlurredLinesMU", "BlurredLines", hasDatabaseGameId: true);

        LegacyMashupBlock block = Assert.Single(mashup.Blocks);
        Assert.Equal(0, block.SourceBlock.DatabaseGameId);
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

        CinematicActorTrailer trailer = CinematicSceneActorReader.ReadActorTrailer(
            new CinematicBinaryReader(stream.ToArray()),
            new LegacyBinarySerializerContext(2014));

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

    private sealed class EngineConditionDefaultModel
    {
        [LegacyBinaryEngineVersionCondition(MaxEngineVersion = 2014)]
        public uint Jd2014OnlyValue { get; set; } = 7;

        public uint AlwaysValue { get; set; }
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

    private static void WriteBlockFlowHeader(Stream stream, int blockCount)
    {
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0x1B857BCE);
        WriteUInt32(stream, 0xAC);
        for (int i = 0; i < 28; i++)
            stream.WriteByte(0);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0x5B648E44);
        WriteUInt32(stream, 0x2C);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, (uint)blockCount);
    }

    private static void WriteJd2014BlockDescriptor(
        Stream stream,
        string songName,
        int databaseGameId,
        int firstBeat,
        int lastBeat,
        float offsetX,
        float offsetY,
        float scale)
    {
        WriteUInt32(stream, 0x194);
        WriteString(stream, songName);
        WriteUInt32(stream, (uint)databaseGameId);
        WriteUInt32(stream, (uint)firstBeat);
        WriteUInt32(stream, (uint)lastBeat);
        WriteUInt32(stream, 0);
        WriteSingle(stream, offsetX);
        WriteSingle(stream, offsetY);
        WriteSingle(stream, scale);
        WriteString(stream, string.Empty);
    }

    private static void WriteJd2015BlockDescriptor(
        Stream stream,
        string songName,
        int firstBeat,
        int lastBeat,
        float offsetX,
        float offsetY,
        float scale,
        bool isEmpty,
        string guid)
    {
        WriteUInt32(stream, 0xBC);
        WriteString(stream, songName);
        WriteUInt32(stream, (uint)firstBeat);
        WriteUInt32(stream, (uint)lastBeat);
        WriteUInt32(stream, 0);
        WriteSingle(stream, offsetX);
        WriteSingle(stream, offsetY);
        WriteSingle(stream, scale);
        WriteString(stream, string.Empty);
        WriteSingle(stream, 1.0f);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, isEmpty ? 1u : 0u);
        WriteUInt32(stream, 0);
        WriteString(stream, guid);
    }

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
