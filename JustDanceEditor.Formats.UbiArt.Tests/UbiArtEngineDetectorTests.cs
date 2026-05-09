using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using System.Buffers.Binary;
using System;
using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtEngineDetectorTests
{
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
        Assert.IsType<UbiArtLayoutResolver>(profile.Layout);
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

            Assert.Equal(UbiArtPlatform.X360, profile.Platform);
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

            Assert.Equal(UbiArtPlatform.Wii, profile.Platform);
            Assert.Equal(UbiArtEngineVersion.JD2019, profile.EngineVersion);
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
    public void Layout_Should_Resolve_JD2014_Uncooked_MapFolder()
    {
        UbiArtLayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "maps", "jd5", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_JD2014_Cooked_MapFolder()
    {
        UbiArtLayoutResolver layout = new();
        string mapFolder = layout.GetMapWorldFolder("/input", "song", UbiArtPlatform.WiiU, UbiArtEngineVersion.JD2014);
        Assert.Equal(Path.Combine("world", "jd5", "song"), mapFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_X360_MovesFolder()
    {
        UbiArtLayoutResolver layout = new();
        string movesFolder = layout.GetMovesFolder("/input", "song", UbiArtPlatform.X360, UbiArtEngineVersion.JD2019);
        Assert.Equal(Path.Combine("world", "maps", "song", "timeline", "moves", "x360"), movesFolder);
    }

    [Fact]
    public void Layout_Should_Resolve_Durango_MovesFolder()
    {
        UbiArtLayoutResolver layout = new();
        string movesFolder = layout.GetMovesFolder("/input", "song", UbiArtPlatform.Durango, UbiArtEngineVersion.JD2021);
        Assert.Equal(Path.Combine("world", "maps", "song", "timeline", "moves", "durango"), movesFolder);
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
