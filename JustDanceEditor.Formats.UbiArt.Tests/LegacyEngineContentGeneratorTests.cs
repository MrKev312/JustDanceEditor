using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using System.Buffers.Binary;
using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

using UbiArtClipTape = JustDanceEditor.Formats.UbiArt.Model.ClipTape;
using UbiArtGameplayEventClip = JustDanceEditor.Formats.UbiArt.Model.Clips.GameplayEventClip;
using UbiArtGoldEffectClip = JustDanceEditor.Formats.UbiArt.Model.Clips.GoldEffectClip;
using UbiArtHideUserInterfaceClip = JustDanceEditor.Formats.UbiArt.Model.Clips.HideUserInterfaceClip;
using UbiArtKaraokeClip = JustDanceEditor.Formats.UbiArt.Model.Clips.KaraokeClip;
using UbiArtMotionClip = JustDanceEditor.Formats.UbiArt.Model.Clips.MotionClip;
using UbiArtPictogramClip = JustDanceEditor.Formats.UbiArt.Model.Clips.PictogramClip;
using UbiArtSoundSetClip = JustDanceEditor.Formats.UbiArt.Model.Clips.SoundSetClip;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class LegacyEngineContentGeneratorTests
{
    [Theory]
    [InlineData(UbiArtPlatform.Wii)]
    [InlineData(UbiArtPlatform.X360)]
    public void Factory_UsesLegacyGenerator_For_JD2019_WiiStylePlatforms(UbiArtPlatform platform)
    {
        UbiArtExporterFactory factory = new();

        IEngineContentGenerator generator = factory.GetEngineContentGenerator(UbiArtEngineVersion.JD2019, platform);

        Assert.IsType<LegacyEngineContentGenerator>(generator);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacySongDesc()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateSongDesc(package));

        SongDesc songDesc = new BinaryUbiArtSerializer().Deserialize<SongDesc>(new MemoryStream(bytes));

        InfoComponent info = songDesc.Components.Single();
        Assert.Equal("TestMap", info.MapName);
        Assert.Equal(UbiArtEngineVersion.JD2019, (UbiArtEngineVersion)info.JDVersion);
        Assert.Equal(2016u, info.OriginalJDVersion);
        Assert.Equal("Artist", info.Artist);
        Assert.Equal("Title", info.Title);
        Assert.Equal(1, info.NumCoach);
        Assert.Equal(2u, info.Difficulty);
        Assert.Equal(0x44 / 255f, info.DefaultColors.Lyrics[0], 5);
        Assert.Equal(0x11 / 255f, info.DefaultColors.Lyrics[1], 5);
        Assert.Equal(0x22 / 255f, info.DefaultColors.Lyrics[2], 5);
        Assert.Equal(0x33 / 255f, info.DefaultColors.Lyrics[3], 5);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyMusicTrack()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMusicTrack(package));

        MusicTrack musicTrack = new BinaryUbiArtSerializer().Deserialize<MusicTrack>(new MemoryStream(bytes));

        Structure structure = musicTrack.Components.Single().TrackData.Structure;
        Assert.Equal(package.TimelineStructure.Markers, structure.Markers);
        Assert.Equal(-1, structure.StartBeat);
        Assert.Equal(2, structure.EndBeat);
        Assert.Single(structure.Signatures);
        Assert.Equal(4, structure.Signatures[0].Beats);
        Assert.Single(structure.Sections);
        Assert.Equal("verse", structure.Sections[0].Comment);
        Assert.Equal("world/maps/testmap/audio/testmap.wav", musicTrack.Components[0].TrackData.Path);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyDanceTape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package));

        UbiArtClipTape tape = new BinaryUbiArtSerializer().Deserialize<UbiArtClipTape>(new MemoryStream(bytes));

        UbiArtMotionClip motion = tape.Clips.OfType<UbiArtMotionClip>().Single();
        UbiArtPictogramClip pictogram = tape.Clips.OfType<UbiArtPictogramClip>().Single();
        UbiArtGoldEffectClip gold = tape.Clips.OfType<UbiArtGoldEffectClip>().Single();

        Assert.Equal("world/maps/testmap/timeline/moves/move_a.msm", motion.ClassifierPath);
        Assert.Equal(1, motion.GoldMove);
        Assert.Equal("world/maps/testmap/timeline/pictos/picto_a.png", pictogram.PictoPath);
        Assert.Equal(1, gold.EffectType);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyKaraokeTape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateKaraokeTape(package));

        UbiArtClipTape tape = new BinaryUbiArtSerializer().Deserialize<UbiArtClipTape>(new MemoryStream(bytes));

        UbiArtKaraokeClip karaoke = tape.Clips.OfType<UbiArtKaraokeClip>().Single();
        Assert.Equal("Test", karaoke.Lyrics);
        Assert.Equal(50, karaoke.StartTime);
        Assert.Equal(20, karaoke.Duration);
        Assert.Equal(1, karaoke.ContentType);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyMainSequenceTape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMainSequenceTape(package));

        UbiArtClipTape tape = new BinaryUbiArtSerializer().Deserialize<UbiArtClipTape>(new MemoryStream(bytes));

        UbiArtSoundSetClip soundSet = tape.Clips.OfType<UbiArtSoundSetClip>().Single();
        UbiArtHideUserInterfaceClip hideHud = tape.Clips.OfType<UbiArtHideUserInterfaceClip>().Single();
        Assert.Equal("world/maps/testmap/audio/amb/amb_testmap_intro.tpl", soundSet.SoundSetPath);
        Assert.Equal(40, hideHud.StartTime);
        Assert.Equal(16, hideHud.Duration);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyGameplayEventClips()
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x01, // Version
            0x00, 0x00, 0x00, 0x00, // Tape version
            0x9E, 0x84, 0x54, 0x60, // Tape type id
            0x00, 0x00, 0x00, 0x9C, // Tape type size
            0x00, 0x00, 0x00, 0x01, // Clip count
            0x10, 0x1F, 0x9D, 0x2B, // Gameplay event clip type id
            0x00, 0x00, 0x00, 0x18, // Serialized clip size
            0x00, 0x00, 0x00, 0x7B, // Id
            0x00, 0x00, 0x01, 0xC8, // Track id
            0x00, 0x00, 0x00, 0x01, // Active
            0x00, 0x00, 0x00, 0x20, // Start time
            0x00, 0x00, 0x00, 0x04  // Duration
        ];

        UbiArtClipTape tape = new BinaryUbiArtSerializer().Deserialize<UbiArtClipTape>(new MemoryStream(bytes));

        UbiArtGameplayEventClip clip = Assert.IsType<UbiArtGameplayEventClip>(Assert.Single(tape.Clips));
        Assert.Equal(123, clip.Id);
        Assert.Equal(456, clip.TrackId);
        Assert.Equal(32, clip.StartTime);
        Assert.Equal(4, clip.Duration);
    }

    [Fact]
    public void GenerateDanceTape_WritesTypedLegacyTapeAndClipLayout()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        object generated = generator.GenerateDanceTape(package);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal((uint)((224 * 3) + 166), ReadUInt32(bytes, 4));
        Assert.Equal(0x9E845460u, ReadUInt32(bytes, 8));
        Assert.Equal(0x9Cu, ReadUInt32(bytes, 12));
        Assert.Equal(3u, ReadUInt32(bytes, 16));
        Assert.Equal(0x955384A1u, ReadUInt32(bytes, 20));
        Assert.Equal(0x70u, ReadUInt32(bytes, 24));
        Assert.Equal(1u, ReadUInt32(bytes, 28));
        Assert.Contains("move_a.msm", ReadAscii(bytes));
        Assert.Contains("picto_a.png", ReadAscii(bytes));
        Assert.True(ContainsUInt32(bytes, 0xFD69B110u));
    }

    [Fact]
    public void GenerateMainSequenceTape_WritesSoundSetAndUiClips()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        object generated = generator.GenerateMainSequenceTape(package);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(2u, ReadUInt32(bytes, 16));
        Assert.Equal(0x2D8C885Bu, ReadUInt32(bytes, 20));
        Assert.Equal(0x40u, ReadUInt32(bytes, 24));
        Assert.Contains("amb_testmap_intro.tpl", ReadAscii(bytes));
        Assert.True(ContainsUInt32(bytes, 0x52E06A9Au));
    }

    [Fact]
    public void GenerateKaraokeTape_WritesTypedClipHeaderOnce()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateKaraokeTape(package));

        Assert.Equal(1u, ReadUInt32(bytes, 16));
        Assert.Equal(0x68552A41u, ReadUInt32(bytes, 20));
        Assert.Equal(0x50u, ReadUInt32(bytes, 24));
        Assert.Equal(5u, ReadUInt32(bytes, 28));
        Assert.Contains("Test", ReadAscii(bytes));
    }

    [Fact]
    public void GenerateMusicTrack_WritesMarkerSignatureAndSectionCounts()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        object generated = generator.GenerateMusicTrack(package);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x1B857BCEu, ReadUInt32(bytes, 8));
        Assert.Equal(3u, ReadUInt32(bytes, 64));
        Assert.Equal(1u, ReadUInt32(bytes, 80));
        Assert.Equal(1u, ReadUInt32(bytes, 96));
        Assert.Contains("testmap.wav", ReadAscii(bytes));
    }

    [Fact]
    public void GenerateSongDesc_WritesOfficialWiiResourceHeaderShape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateSongDesc(package));

        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x1B857BCEu, ReadUInt32(bytes, 8));
        Assert.Equal(0x6Cu, ReadUInt32(bytes, 12));
        Assert.Equal(0x8AC2B5C6u, ReadUInt32(bytes, 48));
        Assert.Equal(0xF4u, ReadUInt32(bytes, 52));
    }

    [Fact]
    public void GenerateMainScene_WritesOfficialWiiSceneHeaderShape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMainScene(package));

        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x0004905Du, ReadUInt32(bytes, 4));
        Assert.Equal(7u, ReadUInt32(bytes, 20));
        Assert.Equal(0x4FA40F09u, ReadUInt32(bytes, 24));

        int cinematicTailOffset = FindPathTailOffset(
            bytes,
            "testmap_mainsequence.tpl",
            "world/maps/testmap/cinematics/");

        Assert.Equal(2u, ReadUInt32(bytes, cinematicTailOffset));
        Assert.Equal(0u, ReadUInt32(bytes, cinematicTailOffset + 4));
        Assert.Equal(1u, ReadUInt32(bytes, cinematicTailOffset + 8));
        Assert.Equal(0x677B269Bu, ReadUInt32(bytes, cinematicTailOffset + 12));
    }

    [Fact]
    public void GenerateMainSequenceTpl_WritesOfficialWiiComponentHeaderShape()
    {
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMainSequenceTpl("TestMap"));

        Assert.Equal(0x1B857BCEu, ReadUInt32(bytes, 8));
        Assert.Equal(0x0C736497u, ReadUInt32(bytes, 48));
        Assert.Equal(0x40u, ReadUInt32(bytes, 52));
        Assert.Equal(0xABF3773Eu, ReadUInt32(bytes, 72));
    }

    [Fact]
    public void GenerateAutodanceScene_WritesOfficialSingleActorFooterShape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateAutodanceScene(package));

        Assert.Equal(0x5Cu, ReadUInt32(bytes, 8));
        Assert.Equal(1u, ReadUInt32(bytes, bytes.Length - 12));
        Assert.Equal(0xF466D41Au, ReadUInt32(bytes, bytes.Length - 8));
        Assert.Equal(0u, ReadUInt32(bytes, bytes.Length - 4));
    }

    [Fact]
    public void GenerateMenuArtActor_WritesOfficialWiiActorHeaderShape()
    {
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtActor("testmap_cover_generic", "TestMap"));

        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x3F800000u, ReadUInt32(bytes, 8));
        Assert.Equal(0x3F800000u, ReadUInt32(bytes, 12));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, 24));
        Assert.Contains("tpl_materialgraphiccomponent2d.tpl", ReadAscii(bytes));

        int shaderTailOffset = FindPathTailOffset(
            bytes,
            "multitexture_1layer.msh",
            "world/_common/matshader/");

        Assert.Equal(0x601C6A28u, ReadUInt32(bytes, shaderTailOffset - sizeof(uint)));
    }

    [Fact]
    public void GenerateGenericActor_WritesOfficialActorHeaderParentOffset()
    {
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(
            generator.GenerateGenericActor("TestActor", "world/maps/testmap/test.tpl"));

        Assert.Equal(0u, ReadUInt32(bytes, 20));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, 24));
        Assert.Equal(0u, ReadUInt32(bytes, 28));
    }

    [Fact]
    public void GenerateVideoScene_WritesOfficialVideoOutputMaterialShape()
    {
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2016);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateVideoScene("TestMap"));

        int shaderOffset = IndexOfAscii(bytes, "pleofullscreen.msh");
        Assert.True(shaderOffset >= 0);
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, shaderOffset - 32));
        Assert.Equal(0u, ReadUInt32(bytes, shaderOffset - 28));
        Assert.Equal(0u, ReadUInt32(bytes, shaderOffset - 24));
        Assert.Equal(0u, ReadUInt32(bytes, shaderOffset - 20));
        Assert.Equal(0u, ReadUInt32(bytes, shaderOffset - 16));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, shaderOffset - 12));
        Assert.Equal(0u, ReadUInt32(bytes, shaderOffset - 8));
        Assert.Equal(0x12u, ReadUInt32(bytes, shaderOffset - 4));
        Assert.Equal(0u, ReadUInt32(bytes, bytes.Length - 20));
        Assert.Equal(0u, ReadUInt32(bytes, bytes.Length - 4));
    }

    [Fact]
    public void ModernGenerateMenuArtActor_ReturnsTypedVersionedBinaryActor()
    {
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateMenuArtActor("testmap_cover_generic", "TestMap");
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x3F800000u, ReadUInt32(bytes, 8));
        Assert.True(ContainsUInt32(bytes, 0xB4A817A8u));
        Assert.True(ContainsUInt32(bytes, 0x72B61FC5u));
        Assert.True(ContainsUInt32(bytes, 0xCA888FC5u));

        int shaderOffset = IndexOfUInt32(bytes, 0xD7E7D9C7u);
        Assert.True(shaderOffset >= 0);
        Assert.Equal(0x3F800000u, ReadUInt32(bytes, shaderOffset + 44));
    }

    [Fact]
    public void Jd2017GenerateMenuArtActor_UsesOldVersionedTailShape()
    {
        JD2017EngineContentGenerator generator = new(UbiArtEngineVersion.JD2017);

        object generated = generator.GenerateMenuArtActor("testmap_cover_generic", "TestMap");
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.True(ContainsUInt32(bytes, 0xB4A817A8u));
        Assert.True(ContainsUInt32(bytes, 0x72B61FC5u));

        int shaderOffset = IndexOfUInt32(bytes, 0xD7E7D9C7u);
        Assert.True(shaderOffset >= 0);
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, shaderOffset + 16));
    }

    [Fact]
    public void UncookedGenerateMenuArtActor_ReturnsTextForWriteEdgeEncoding()
    {
        UncookedEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateMenuArtActor("testmap_cover_generic", "TestMap");
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsType<string>(generated);
        Assert.Contains("world/maps/testmap/menuart/actors/testmap_cover_generic.tpl", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ModernGenerateVideoPlayerActor_ReturnsTypedBinaryActor()
    {
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateVideoPlayerActor("TestMap", isPreview: false);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.True(ContainsUInt32(bytes, 0xF5D5E8F2u));
        Assert.True(ContainsUInt32(bytes, 0x1263DAD9u));
        Assert.True(ContainsUInt32(bytes, 0x560FF17Au));
        Assert.True(ContainsUInt32(bytes, 0x268C7614u));
        Assert.Contains("testmap.webm", ReadAscii(bytes));
        Assert.Contains("testmap.mpd", ReadAscii(bytes));
    }

    [Fact]
    public void ModernGenerateVideoPlayerPreviewActor_WritesPreviewTemplateAndChannel()
    {
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateVideoPlayerActor("TestMap", isPreview: true);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.True(ContainsUInt32(bytes, 0xD3945428u));
        Assert.Contains("video_player_map_preview.tpl", ReadAscii(bytes));
        Assert.Contains("TestMap", ReadAscii(bytes));
    }

    [Fact]
    public void ModernGenerateAutodanceActor_ReturnsTypedBinaryActor()
    {
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateAutodanceActor("TestMap");
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.True(ContainsUInt32(bytes, 0xD750313Cu));
        Assert.True(ContainsUInt32(bytes, 0x67B8BB77u));
        Assert.Contains("testmap_autodance.tpl", ReadAscii(bytes));
    }

    [Fact]
    public void ModernGenerateMpd_ReturnsTypedBinaryFile()
    {
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2020);

        object generated = generator.GenerateMpd();
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generated);

        Assert.IsNotType<byte[]>(generated);
        Assert.Equal(17, bytes.Length);
        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x00424BAEu, ReadUInt32(bytes, 4));
        Assert.Equal(0x14, bytes[8]);
        Assert.Equal(0x3F800000u, ReadUInt32(bytes, 9));
    }

    private static IntermediateSongPackage CreatePackage() => new()
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

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));

    private static bool ContainsUInt32(byte[] bytes, uint value)
    {
        return IndexOfUInt32(bytes, value) >= 0;
    }

    private static int IndexOfUInt32(byte[] bytes, uint value)
    {
        for (int i = 0; i <= bytes.Length - sizeof(uint); i++)
        {
            if (ReadUInt32(bytes, i) == value)
                return i;
        }

        return -1;
    }

    private static int IndexOfAscii(byte[] bytes, string value)
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

    private static int FindPathTailOffset(byte[] bytes, string fileName, string folder)
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

    private static void AssertAsciiAt(byte[] bytes, int offset, string value)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(value);
        for (int i = 0; i < pattern.Length; i++)
            Assert.Equal(pattern[i], bytes[offset + i]);
    }

    private static string ReadAscii(byte[] bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] is >= 32 and <= 126 ? (char)bytes[i] : '.';

        return new string(chars);
    }

    private static string FindRepoRoot()
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
