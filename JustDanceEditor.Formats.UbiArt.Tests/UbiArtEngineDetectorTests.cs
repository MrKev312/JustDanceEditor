using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtEngineDetectorTests
{
    [Fact]
    public void Detect_PrefersUncookedProject_WhenCookedCacheAlsoExists()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "world", "maps", "song"));
            Directory.CreateDirectory(Path.Combine(root, "cache", "itf_cooked", "nx", "world", "maps", "song"));

            UbiArtVersionProfile profile = new UbiArtEngineDetector().Detect(root);

            Assert.Equal(UbiArtPlatform.Uncooked, profile.Platform);
            Assert.IsType<UbiArtLayoutResolver>(profile.Layout);
            Assert.IsType<LuaUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_ModernCooked_Should_Peek_JDVersion_From_SongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mapsFolder = Path.Combine(root, "world", "maps", "song");
        Directory.CreateDirectory(mapsFolder);

        // Create a JSON songdesc with JDVersion
        File.WriteAllText(Path.Combine(mapsFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"JDVersion\": 4884 } ] }");

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtPlatform.Uncooked, profile.Platform);
        Assert.Equal(UbiArtEngineVersion.JD2022, profile.EngineVersion);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Detect_JD2014_Uncooked_When_world_maps_jd5_exists()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "world", "maps", "jd5"));

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtPlatform.Uncooked, profile.Platform);
        Assert.Equal(UbiArtEngineVersion.JD2014, profile.EngineVersion);
        Assert.IsType<JD2014LayoutResolver>(profile.Layout);
        Assert.IsType<LuaUbiArtSerializer>(profile.Serializer);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Detect_Uncooked_When_flat_and_tpl_exists()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "songdesc.tpl"), "params = {}\n");

        UbiArtEngineDetector detector = new();
        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtPlatform.Uncooked, profile.Platform);
        Assert.Equal(UbiArtEngineVersion.JD2022, profile.EngineVersion);
        Assert.IsType<UbiArtLayoutResolver>(profile.Layout);
        Assert.IsType<LuaUbiArtSerializer>(profile.Serializer);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Detect_X360LegacyCooked_Should_Read_JD2019_From_Sibling_Bundle()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string songRoot = Path.Combine(root, "pocoloco_x360");
        string cookedMap = Path.Combine(songRoot, "cache", "itf_cooked", "x360", "world", "maps", "pocoloco", "timeline");
        string legacyData = Path.Combine(root, "Bundle_X360", "cache", "itf_cooked", "x360", "cache", "legacyconverteddata", "pocoloco");

        Directory.CreateDirectory(cookedMap);
        Directory.CreateDirectory(legacyData);
        File.WriteAllBytes(Path.Combine(cookedMap, "pocoloco_tml_dance.dtape.ckd"), [0, 0, 0, 1, 0, 0, 0, 0x9C]);
        File.WriteAllBytes(Path.Combine(legacyData, "songdesc.main_legacy.tpl.ckd"), CreateLegacySongDesc("PocoLoco", UbiArtEngineVersion.JD2019));

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(songRoot);

            Assert.Equal(UbiArtPlatform.Xenon, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2019, profile.EngineVersion);
            Assert.IsType<BinaryUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_WiiLegacyCooked_Should_Read_JD2019_From_LegacySongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string cookedMap = Path.Combine(root, "cache", "itf_cooked", "wii", "world", "maps", "song", "timeline");
        string legacyData = Path.Combine(root, "cache", "itf_cooked", "wii", "cache", "legacyconverteddata", "song");

        Directory.CreateDirectory(cookedMap);
        Directory.CreateDirectory(legacyData);
        File.WriteAllBytes(Path.Combine(cookedMap, "song_tml_dance.dtape.ckd"), [0, 0, 0, 1, 0, 0, 0, 0x9C]);
        File.WriteAllBytes(Path.Combine(legacyData, "songdesc.main_legacy.tpl.ckd"), CreateLegacySongDesc("Song", UbiArtEngineVersion.JD2019));

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(root);

            Assert.Equal(UbiArtPlatform.Revolution, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2019, profile.EngineVersion);
            Assert.IsType<BinaryUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_Ps3LegacyCooked_Should_Read_JD2018_From_LegacySongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string cookedMap = Path.Combine(root, "cache", "itf_cooked", "ps3", "world", "maps", "song", "timeline");
        string legacyData = Path.Combine(root, "cache", "itf_cooked", "ps3", "cache", "legacyconverteddata", "song");

        Directory.CreateDirectory(cookedMap);
        Directory.CreateDirectory(legacyData);
        File.WriteAllBytes(Path.Combine(cookedMap, "song_tml_dance.dtape.ckd"), [0, 0, 0, 1, 0, 0, 0, 0x9C]);
        File.WriteAllBytes(Path.Combine(legacyData, "songdesc.main_legacy.tpl.ckd"), CreateLegacySongDesc("Song", UbiArtEngineVersion.JD2018));

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(root);

            Assert.Equal(UbiArtPlatform.Cell, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2018, profile.EngineVersion);
            Assert.IsType<BinaryUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_DurangoCooked_Should_Read_JD2021_From_SongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string cookedMap = Path.Combine(root, "cache", "itf_cooked", "durango", "world", "maps", "dancemonkey");
        Directory.CreateDirectory(cookedMap);

        File.WriteAllText(Path.Combine(cookedMap, "songdesc.tpl.ckd"), "{ \"COMPONENTS\": [ { \"JDVersion\": 2021, \"OriginalJDVersion\": 2021, \"MapName\": \"DanceMonkey\" } ] }\0");

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(root);

            Assert.Equal(UbiArtPlatform.Durango, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2021, profile.EngineVersion);
            Assert.IsType<JsonUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_ModernCooked_Should_Use_Highest_SongDesc_Version_Across_Maps()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mapsRoot = Path.Combine(root, "cache", "itf_cooked", "durango", "world", "maps");
        string olderMap = Path.Combine(mapsRoot, "z_oldmap");
        string newerMap = Path.Combine(mapsRoot, "a_newmap");
        Directory.CreateDirectory(olderMap);
        Directory.CreateDirectory(newerMap);

        File.WriteAllText(Path.Combine(olderMap, "songdesc.tpl.ckd"), "{ \"COMPONENTS\": [ { \"JDVersion\": 2020, \"OriginalJDVersion\": 2020 } ] }\0");
        File.WriteAllText(Path.Combine(newerMap, "songdesc.tpl.ckd"), "{ \"COMPONENTS\": [ { \"JDVersion\": 2022, \"OriginalJDVersion\": 2022 } ] }\0");

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(root);

            Assert.Equal(UbiArtPlatform.Durango, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2022, profile.EngineVersion);
            Assert.IsType<JsonUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_OrbisCooked_Should_Read_JD2022_From_SongDesc()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string cookedMap = Path.Combine(root, "cache", "itf_cooked", "orbis", "world", "maps", "adventurerkids");
        Directory.CreateDirectory(cookedMap);

        File.WriteAllText(Path.Combine(cookedMap, "songdesc.tpl.ckd"), "{ \"COMPONENTS\": [ { \"JDVersion\": 2022, \"OriginalJDVersion\": 2022, \"MapName\": \"AdventurerKids\" } ] }\0");

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(root);

            Assert.Equal(UbiArtPlatform.Orbis, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2022, profile.EngineVersion);
            Assert.IsType<JsonUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_X360LegacyCooked_Should_Use_Highest_LegacySongDesc_From_Sibling_Bundle()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string songRoot = Path.Combine(root, "legacy_x360");
        string cookedMap = Path.Combine(songRoot, "cache", "itf_cooked", "x360", "world", "maps", "legacy", "timeline");
        string olderData = Path.Combine(root, "Bundle_X360", "cache", "itf_cooked", "x360", "cache", "legacyconverteddata", "z_oldmap");
        string newerData = Path.Combine(root, "Bundle_X360", "cache", "itf_cooked", "x360", "cache", "legacyconverteddata", "a_newmap");

        Directory.CreateDirectory(cookedMap);
        Directory.CreateDirectory(olderData);
        Directory.CreateDirectory(newerData);
        File.WriteAllBytes(Path.Combine(cookedMap, "legacy_tml_dance.dtape.ckd"), [0, 0, 0, 1, 0, 0, 0, 0x9C]);
        File.WriteAllBytes(Path.Combine(olderData, "songdesc.main_legacy.tpl.ckd"), CreateLegacySongDesc("OldMap", UbiArtEngineVersion.JD2018));
        File.WriteAllBytes(Path.Combine(newerData, "songdesc.main_legacy.tpl.ckd"), CreateLegacySongDesc("NewMap", UbiArtEngineVersion.JD2019));

        try
        {
            UbiArtEngineDetector detector = new();
            UbiArtVersionProfile profile = detector.Detect(songRoot);

            Assert.Equal(UbiArtPlatform.Xenon, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2019, profile.EngineVersion);
            Assert.IsType<BinaryUbiArtSerializer>(profile.Serializer);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Layout_Should_Resolve_JD2014_Uncooked_MapFolder()
    {
        JD2014LayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "maps", "jd5", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_JD2014_Cooked_MapFolder()
    {
        JD2014LayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "jd5", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_JD2015_Cooked_MapFolder()
    {
        JD2015LayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2015);
        Assert.Equal(Path.Combine("world", "jd2015", "song"), mapFolder);
    }

    [Fact]
    public void DefaultLayout_Should_Resolve_Modern_MapFolder()
    {
        UbiArtLayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "maps", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_X360_MovesFolder()
    {
        UbiArtLayoutResolver layout = new();
        string movesFolder = layout.GetMovesFolder("/input", "song", UbiArtPlatform.Xenon, UbiArtEngineVersion.JD2019);
        Assert.Equal(Path.Combine("world", "maps", "song", "timeline", "moves", "x360"), movesFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_Durango_MovesFolder()
    {
        UbiArtLayoutResolver layout = new();
        string movesFolder = layout.GetMovesFolder("/input", "song", UbiArtPlatform.Durango, UbiArtEngineVersion.JD2021);
        Assert.Equal(Path.Combine("world", "maps", "song", "timeline", "moves", "durango"), movesFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_Orbis_MovesFolder()
    {
        UbiArtLayoutResolver layout = new();
        string movesFolder = layout.GetMovesFolder("/input", "song", UbiArtPlatform.Orbis, UbiArtEngineVersion.JD2022);
        Assert.Equal(Path.Combine("world", "maps", "song", "timeline", "moves", "orbis"), movesFolder);
    }

    private static byte[] CreateLegacySongDesc(string mapName, UbiArtEngineVersion engineVersion)
    {
        using MemoryStream stream = new();

        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0x304);
        WriteUInt32(stream, 0x1B857BCE);
        WriteUInt32(stream, 0x6C);
        stream.Write(new byte[28]);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 0x8AC2B5C6);
        WriteUInt32(stream, 0xF4);
        WriteString(stream, mapName);
        WriteUInt32(stream, (uint)engineVersion);
        WriteUInt32(stream, (uint)engineVersion);

        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}