using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.LegacyEngineContentGeneratorTestHelpers;

using UbiArtClipTape = JustDanceEditor.Formats.UbiArt.Model.ClipTape;
using UbiArtGoldEffectClip = JustDanceEditor.Formats.UbiArt.Model.Clips.GoldEffectClip;
using UbiArtHideUserInterfaceClip = JustDanceEditor.Formats.UbiArt.Model.Clips.HideUserInterfaceClip;
using UbiArtKaraokeClip = JustDanceEditor.Formats.UbiArt.Model.Clips.KaraokeClip;
using UbiArtMotionClip = JustDanceEditor.Formats.UbiArt.Model.Clips.MotionClip;
using UbiArtPictogramClip = JustDanceEditor.Formats.UbiArt.Model.Clips.PictogramClip;
using UbiArtSoundSetClip = JustDanceEditor.Formats.UbiArt.Model.Clips.SoundSetClip;
using UbiArtTapeReferenceClip = JustDanceEditor.Formats.UbiArt.Model.Clips.TapeReferenceClip;
using UbiArtVibrationClip = JustDanceEditor.Formats.UbiArt.Model.Clips.VibrationClip;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class LegacyEngineContentGeneratorTests
{
    [Theory]
    [InlineData(UbiArtPlatform.Revolution)]
    [InlineData(UbiArtPlatform.Xenon)]
    public void Factory_UsesLegacyGenerator_For_JD2019_WiiStylePlatforms(UbiArtPlatform platform)
    {
        UbiArtExporterFactory factory = new();

        IEngineContentGenerator generator = factory.GetEngineContentGenerator(UbiArtEngineVersion.JD2019, platform);

        Assert.IsType<LegacyEngineContentGenerator>(generator);
    }

    [Theory]
    [InlineData(UbiArtEngineVersion.JD2014, UbiArtPlatform.Cafe)]
    [InlineData(UbiArtEngineVersion.JD2014, UbiArtPlatform.Xenon)]
    [InlineData(UbiArtEngineVersion.JD2015, UbiArtPlatform.Revolution)]
    [InlineData(UbiArtEngineVersion.JD2015, UbiArtPlatform.Cell)]
    [InlineData(UbiArtEngineVersion.JD2015, UbiArtPlatform.Cafe)]
    [InlineData(UbiArtEngineVersion.JD2015, UbiArtPlatform.Xenon)]
    [InlineData(UbiArtEngineVersion.JD2015, UbiArtPlatform.Orbis)]
    public void Factory_UsesLegacyGenerator_For_JD2014AndJD2015_CookedPlatforms(UbiArtEngineVersion version, UbiArtPlatform platform)
    {
        UbiArtExporterFactory factory = new();

        IEngineContentGenerator generator = factory.GetEngineContentGenerator(version, platform);

        Assert.IsType<LegacyEngineContentGenerator>(generator);
    }

    [Fact]
    public void LegacyJD2015_GeneratesVersionedMapAndCommonPaths()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2015, UbiArtPlatform.Cafe);

        string musicTrack = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMusicTrack(package)));
        string danceTape = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package)));
        string mainSequenceTape = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMainSequenceTape(package)));
        string videoScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateVideoScene("TestMap")));
        string menuArtActor = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtActor("testmap_cover_generic", "TestMap")));

        Assert.Contains("testmap.wav", musicTrack);
        Assert.Contains("world/jd2015/testmap/audio/", musicTrack);
        Assert.Contains("move_a.msm", danceTape);
        Assert.Contains("world/jd2015/testmap/timeline/moves/", danceTape);
        Assert.Contains("picto_a.png", danceTape);
        Assert.Contains("world/jd2015/testmap/timeline/pictos/", danceTape);
        Assert.Contains("amb_testmap_intro.tpl", mainSequenceTape);
        Assert.Contains("world/jd2015/testmap/audio/amb/", mainSequenceTape);
        Assert.Contains("world/jd2015/_common/videoscreen/", videoScene);
        Assert.Contains("testmap.webm", videoScene);
        Assert.Contains("world/jd2015/testmap/videoscoach/", videoScene);
        Assert.Contains("world/jd2015/_common/matshader/", menuArtActor);
        Assert.Contains("testmap_cover_generic.tga", menuArtActor);
        Assert.Contains("world/jd2015/testmap/menuart/textures/", menuArtActor);
        Assert.DoesNotContain("world/maps/testmap", musicTrack + danceTape + mainSequenceTape + videoScene + menuArtActor);
        Assert.DoesNotContain("world/_common", videoScene + menuArtActor);
    }

    [Fact]
    public void LegacyJD2014_GeneratesJd5MapAndCommonPaths()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2014, UbiArtPlatform.Cafe);

        string musicTrack = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMusicTrack(package)));
        string danceTape = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package)));
        string videoScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateVideoScene("TestMap")));
        string mainScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMainScene(package)));
        string audioScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateAudioScene(package)));

        Assert.Contains("testmap.wav", musicTrack);
        Assert.Contains("world/jd5/testmap/audio/", musicTrack);
        Assert.Contains("move_a.msm", danceTape);
        Assert.Contains("world/jd5/testmap/timeline/moves/", danceTape);
        Assert.Contains("picto_a.png", danceTape);
        Assert.Contains("world/jd5/testmap/timeline/pictos/", danceTape);
        Assert.Contains("world/jd5/_common/videoscreen/", videoScene);
        Assert.Contains("testmap.webm", videoScene);
        Assert.Contains("world/jd5/testmap/videoscoach/", videoScene);
        Assert.Contains("songdesc.tpl", mainScene);
        Assert.Contains("world/jd5/testmap/", mainScene);
        Assert.DoesNotContain("legacyconverteddata", mainScene);
        Assert.DoesNotContain("sequence", audioScene, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("world/maps/testmap", musicTrack + danceTape + videoScene + mainScene);
        Assert.DoesNotContain("world/_common", videoScene);
    }

    [Fact]
    public void BinarySerializer_DeserializesGeneratedJd2014PackedTimeline()
    {
        IntermediateSongPackage package = CreatePackage();
        package.FullBodyCoachTimelines.Add(new MoveTimeline
        {
            CoachId = 1,
            Clips =
            [
                new MoveClip { Id = 6, StartTime = 60, MoveId = "gesture_a" }
            ]
        });
        package.FullBodyCoachMoves["gesture_a"] = new CoachMoveDefinition
        {
            Duration = 18,
            MoveType = CoachMoveType.FullBodyTracking
        };

        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2014, UbiArtPlatform.Revolution);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateJd2014Timeline(package));

        Assert.Equal(1u, ReadUInt32(bytes, 0));
        Assert.Equal(0x1B857BCEu, ReadUInt32(bytes, 8));
        Assert.Equal(0xACu, ReadUInt32(bytes, 12));
        Assert.Equal(0x109FBC33u, ReadUInt32(bytes, 48));

        LegacyJd2014Timeline timeline = new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2014)
            .Deserialize<LegacyJd2014Timeline>(new MemoryStream(bytes));

        Assert.Equal("TestMap", timeline.MapName);
        UbiArtMotionClip handMove = timeline.DanceTape.Clips.OfType<UbiArtMotionClip>().Single(clip => clip.ClassifierPath.EndsWith("move_a.msm", StringComparison.Ordinal));
        UbiArtMotionClip fullBodyMove = timeline.DanceTape.Clips.OfType<UbiArtMotionClip>().Single(clip => clip.ClassifierPath.EndsWith("gesture_a.gesture", StringComparison.Ordinal));
        UbiArtPictogramClip pictogram = timeline.DanceTape.Clips.OfType<UbiArtPictogramClip>().Single();
        UbiArtGoldEffectClip gold = timeline.DanceTape.Clips.OfType<UbiArtGoldEffectClip>().Single();
        UbiArtKaraokeClip karaoke = timeline.KaraokeTape.Clips.OfType<UbiArtKaraokeClip>().Single();

        Assert.Equal("world/jd5/testmap/timeline/moves/move_a.msm", handMove.ClassifierPath);
        Assert.Equal(0, handMove.MoveType);
        Assert.Equal("world/jd5/testmap/timeline/moves/gesture_a.gesture", fullBodyMove.ClassifierPath);
        Assert.Equal(1, fullBodyMove.MoveType);
        Assert.Equal("world/jd5/testmap/timeline/pictos/picto_a.tga", pictogram.PictoPath);
        Assert.Equal(30, gold.StartTime);
        Assert.Equal(8, gold.Duration);
        Assert.Equal("Test", karaoke.Lyrics);
    }

    [Fact]
    public void LegacyMenuArtScene_UsesCoachCountAndSkipsOnlineCovers()
    {
        IntermediateSongPackage package = CreatePackage();
        package.Metadata.CoachCount = 2;
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2015, UbiArtPlatform.Xenon);

        string menuArtScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtScene(package)));

        Assert.Contains("TestMap_cover_generic", menuArtScene);
        Assert.Contains("TestMap_cover_albumcoach", menuArtScene);
        Assert.Contains("TestMap_cover_albumbkg", menuArtScene);
        Assert.Contains("TestMap_coach_1", menuArtScene);
        Assert.Contains("TestMap_coach_2", menuArtScene);
        Assert.DoesNotContain("cover_online", menuArtScene, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestMap_coach_3", menuArtScene);
        Assert.DoesNotContain("TestMap_coach_4", menuArtScene);
    }

    [Fact]
    public void LegacyJD2014MenuArtScene_SkipsGenericCover()
    {
        IntermediateSongPackage package = CreatePackage();
        package.Metadata.CoachCount = 4;
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2014, UbiArtPlatform.Revolution);

        string menuArtScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtScene(package)));

        Assert.DoesNotContain("TestMap_cover_generic", menuArtScene);
        Assert.Contains("TestMap_cover_albumcoach", menuArtScene);
        Assert.Contains("TestMap_cover_albumbkg", menuArtScene);
        Assert.Contains("TestMap_coach_1", menuArtScene);
        Assert.Contains("TestMap_coach_4", menuArtScene);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacySongDesc()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateSongDesc(package));

        SongDesc songDesc = (SongDesc)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacySongDesc>(new MemoryStream(bytes));

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
    public void BinarySerializer_DeserializesJd2014SongDesc()
    {
        IntermediateSongPackage package = CreatePackage();
        package.Metadata.OriginalJDVersion = 2014;
        package.Metadata.CoachCount = 4;
        package.Metadata.Difficulty = 1;

        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2014);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateSongDesc(package));

        Assert.Equal(0xACu, ReadUInt32(bytes, 0x0C));
        Assert.Equal(0x104u, ReadUInt32(bytes, 0x34));
        Assert.True(ContainsUInt32(bytes, 0x00000005));
        Assert.Equal(-1, IndexOfAscii(bytes, "Unknown Dancer"));

        SongDesc songDesc = (SongDesc)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2014)
            .Deserialize<LegacySongDesc>(new MemoryStream(bytes));

        InfoComponent info = songDesc.Components.Single();
        Assert.Equal("TestMap", info.MapName);
        Assert.Equal(2014u, info.JDVersion);
        Assert.Equal(2014u, info.OriginalJDVersion);
        Assert.Equal("Artist", info.Artist);
        Assert.Equal("Title", info.Title);
        Assert.Equal(4, info.NumCoach);
        Assert.Equal(1u, info.Difficulty);
        Assert.Equal(1, info.MainCoach);
        Assert.Equal(0u, info.SweatDifficulty);
        Assert.Equal(1, info.LocaleID);
    }

    [Fact]
    public void BinarySerializer_DeserializesOfficialJd2014SongDescEntryList()
    {
        byte[] bytes = CreateOfficialJd2014SongDescWithRelatedAlbumsAndColoredEntries();

        SongDesc songDesc = (SongDesc)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2014)
            .Deserialize<LegacySongDesc>(new MemoryStream(bytes));

        InfoComponent info = songDesc.Components.Single();
        Assert.Equal("VsMap", info.MapName);
        Assert.Equal(2014u, info.JDVersion);
        Assert.Equal(2014u, info.OriginalJDVersion);
        Assert.Equal("Artist With Parents", info.Artist);
        Assert.Equal("Title With Colored Entry", info.Title);
        Assert.Equal(4, info.NumCoach);
        Assert.Equal(3u, info.Difficulty);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyMusicTrack()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateMusicTrack(package));

        MusicTrack musicTrack = (MusicTrack)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyMusicTrack>(new MemoryStream(bytes));

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
        package.CoachTimelines[0].TrackId = 456;
        package.GoldEffects.Clips[0].TrackId = 789;
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package));

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyClipTape>(new MemoryStream(bytes));

        UbiArtMotionClip motion = tape.Clips.OfType<UbiArtMotionClip>().Single();
        UbiArtPictogramClip pictogram = tape.Clips.OfType<UbiArtPictogramClip>().Single();
        UbiArtGoldEffectClip gold = tape.Clips.OfType<UbiArtGoldEffectClip>().Single();

        Assert.Equal("world/maps/testmap/timeline/moves/move_a.msm", motion.ClassifierPath);
        Assert.Equal(456u, motion.TrackId);
        Assert.Equal(1, motion.GoldMove);
        Assert.Equal("world/maps/testmap/timeline/pictos/picto_a.png", pictogram.PictoPath);
        Assert.Equal(789u, gold.TrackId);
        Assert.Equal(1, gold.EffectType);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyDanceTapeWithFullBodyMoves()
    {
        IntermediateSongPackage package = CreatePackage();
        package.FullBodyCoachTimelines.Add(new MoveTimeline
        {
            CoachId = 0,
            TrackId = 654,
            Clips =
            [
                new MoveClip { Id = 6, StartTime = 60, MoveId = "gesture_a" }
            ]
        });
        package.FullBodyCoachMoves["gesture_a"] = new CoachMoveDefinition
        {
            Duration = 18,
            Color = "#556677",
            MoveType = CoachMoveType.FullBodyTracking
        };

        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2015, UbiArtPlatform.Xenon);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package));

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2015)
            .Deserialize<LegacyClipTape>(new MemoryStream(bytes));

        UbiArtMotionClip[] motions = [.. tape.Clips.OfType<UbiArtMotionClip>()];
        UbiArtMotionClip handMove = motions.Single(clip => clip.ClassifierPath.EndsWith("move_a.msm", StringComparison.Ordinal));
        UbiArtMotionClip fullBodyMove = motions.Single(clip => clip.ClassifierPath.EndsWith("gesture_a.gesture", StringComparison.Ordinal));

        Assert.Equal("world/jd2015/testmap/timeline/moves/move_a.msm", handMove.ClassifierPath);
        Assert.Equal(0, handMove.MoveType);
        Assert.Equal("world/jd2015/testmap/timeline/moves/gesture_a.gesture", fullBodyMove.ClassifierPath);
        Assert.Equal(654u, fullBodyMove.TrackId);
        Assert.Equal(1, fullBodyMove.MoveType);
        Assert.InRange(fullBodyMove.Color[0], 0.999f, 1.001f);
        Assert.InRange(fullBodyMove.Color[1], (0x55 / 255f) - 0.001f, (0x55 / 255f) + 0.001f);
        Assert.InRange(fullBodyMove.Color[2], (0x66 / 255f) - 0.001f, (0x66 / 255f) + 0.001f);
        Assert.InRange(fullBodyMove.Color[3], (0x77 / 255f) - 0.001f, (0x77 / 255f) + 0.001f);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyKaraokeTape()
    {
        IntermediateSongPackage package = CreatePackage();
        LegacyEngineContentGenerator generator = new(UbiArtEngineVersion.JD2019);
        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateKaraokeTape(package));

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyClipTape>(new MemoryStream(bytes));

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

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyClipTape>(new MemoryStream(bytes));

        UbiArtSoundSetClip soundSet = tape.Clips.OfType<UbiArtSoundSetClip>().Single();
        UbiArtHideUserInterfaceClip hideHud = tape.Clips.OfType<UbiArtHideUserInterfaceClip>().Single();
        Assert.Equal("world/maps/testmap/audio/amb/amb_testmap_intro.tpl", soundSet.SoundSetPath);
        Assert.Equal(40, hideHud.StartTime);
        Assert.Equal(16, hideHud.Duration);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyVibrationClips()
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x01, // Version
            0x00, 0x00, 0x00, 0x00, // Tape version
            0x9E, 0x84, 0x54, 0x60, // Tape type id
            0x00, 0x00, 0x00, 0x9C, // Tape type size
            0x00, 0x00, 0x00, 0x01, // Clip count
            0x10, 0x1F, 0x9D, 0x2B, // Vibration clip type id
            0x00, 0x00, 0x00, 0x18, // Serialized clip size
            0x00, 0x00, 0x00, 0x7B, // Id
            0x00, 0x00, 0x01, 0xC8, // Track id
            0x00, 0x00, 0x00, 0x01, // Active
            0x00, 0x00, 0x00, 0x20, // Start time
            0x00, 0x00, 0x00, 0x04  // Duration
        ];

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyClipTape>(new MemoryStream(bytes));

        UbiArtVibrationClip clip = Assert.IsType<UbiArtVibrationClip>(Assert.Single(tape.Clips));
        Assert.Equal(123, clip.Id);
        Assert.Equal(456, clip.TrackId);
        Assert.Equal(32, clip.StartTime);
        Assert.Equal(4, clip.Duration);
        Assert.Equal("world/_common/hd_rumble/bigpulse_01.vib", clip.VibrationFilePath);
        Assert.Equal(-1, clip.PlayerId);
        Assert.Equal(0.5f, clip.Modulation);
    }

    [Fact]
    public void BinarySerializer_DeserializesLegacyTapeReferenceClips()
    {
        using MemoryStream stream = new();
        WriteUInt32(stream, 1); // Version
        WriteUInt32(stream, 0); // Tape version
        WriteUInt32(stream, 0x9E845460); // Tape type id
        WriteUInt32(stream, 0x9C); // Tape type size
        WriteUInt32(stream, 1); // Clip count
        WriteUInt32(stream, 0x0E1E8158); // Tape reference clip type id
        WriteUInt32(stream, 0x54); // Serialized clip size
        WriteUInt32(stream, 0x7B); // Id
        WriteUInt32(stream, 0x1C8); // Track id
        WriteUInt32(stream, 1); // Active
        WriteUInt32(stream, 0x20); // Start time
        WriteUInt32(stream, 0x04); // Duration
        WriteString(stream, "test_vib.tape\0");
        WriteString(stream, "world/maps/test/cinematics/\0");
        WriteUInt32(stream, 0); // Resource id
        WriteUInt32(stream, 1); // Loop
        WriteUInt32(stream, 0); // Padding
        WriteUInt32(stream, 0); // Padding

        UbiArtClipTape tape = (UbiArtClipTape)new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2019)
            .Deserialize<LegacyClipTape>(new MemoryStream(stream.ToArray()));

        UbiArtTapeReferenceClip clip = Assert.IsType<UbiArtTapeReferenceClip>(Assert.Single(tape.Clips));
        Assert.Equal(123, clip.Id);
        Assert.Equal(456, clip.TrackId);
        Assert.Equal(32, clip.StartTime);
        Assert.Equal(4, clip.Duration);
        Assert.Equal("world/maps/test/cinematics/test_vib.tape", clip.Path);
        Assert.Equal(1, clip.Loop);
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

    [Theory]
    [InlineData(UbiArtEngineVersion.JD2017)]
    [InlineData(UbiArtEngineVersion.JD2018)]
    [InlineData(UbiArtEngineVersion.JD2019)]
    [InlineData(UbiArtEngineVersion.JD2020)]
    [InlineData(UbiArtEngineVersion.JD2021)]
    [InlineData(UbiArtEngineVersion.JD2022)]
    public void ModernSceneGenerators_UseCurrentSceneShape(UbiArtEngineVersion version)
    {
        const string expectedSceneEngineVersion = "326704";
        const bool expectPopupAttribute = true;
        const string expectedEnabledAttribute = "DEFAULTENABLE=\"1\"";

        IntermediateSongPackage package = CreatePackage();
        ModernEngineContentGenerator generator = new(version);

        string mainScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMainScene(package)));
        string menuArtScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtScene(package)));

        Assert.Contains($"ENGINE_VERSION=\"{expectedSceneEngineVersion}\"", mainScene);
        Assert.Contains($"ENGINE_VERSION=\"{expectedSceneEngineVersion}\"", menuArtScene);
        Assert.Equal(expectPopupAttribute, mainScene.Contains("isPopup=\"0\""));
        Assert.Equal(expectPopupAttribute, menuArtScene.Contains("isPopup=\"0\""));

        if (expectedEnabledAttribute.Length == 0)
        {
            Assert.DoesNotContain("isEnabled=\"1\"", mainScene);
            Assert.DoesNotContain("DEFAULTENABLE=\"1\"", mainScene);
            Assert.DoesNotContain("isEnabled=\"1\"", menuArtScene);
            Assert.DoesNotContain("DEFAULTENABLE=\"1\"", menuArtScene);
        }
        else
        {
            Assert.Contains(expectedEnabledAttribute, mainScene);
            Assert.Contains(expectedEnabledAttribute, menuArtScene);
        }
    }

    [Theory]
    [InlineData(UbiArtEngineVersion.JD2018, true)]
    [InlineData(UbiArtEngineVersion.JD2019, true)]
    [InlineData(UbiArtEngineVersion.JD2020, true)]
    [InlineData(UbiArtEngineVersion.JD2021, true)]
    [InlineData(UbiArtEngineVersion.JD2022, true)]
    public void ModernMenuArtScene_UsesMapBackgroundOnlyWhenSupported(UbiArtEngineVersion version, bool expectedMapBackground)
    {
        IntermediateSongPackage package = CreatePackage();
        ModernEngineContentGenerator generator = new(version);

        string menuArtScene = ReadAscii(UbiArtEngineContentSerializer.Serialize(generator.GenerateMenuArtScene(package)));

        Assert.Equal(expectedMapBackground, menuArtScene.Contains("TestMap_map_bkg", StringComparison.Ordinal));
        Assert.Contains("TestMap_banner_bkg", menuArtScene);
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
        UncookedEngineContentGenerator generator = new();

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
}