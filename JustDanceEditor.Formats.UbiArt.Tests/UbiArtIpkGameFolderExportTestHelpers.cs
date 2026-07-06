using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;
using JustDanceEditor.Formats.UbiArt.Serialization;

using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Ipk;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

internal static class UbiArtIpkGameFolderExportTestHelpers
{
    internal static IntermediateSongPackage CreatePackage(string mapName, uint originalJDVersion = 0) => new()
    {
        Metadata = new IntermediateMetadata
        {
            MapName = mapName,
            Title = mapName,
            CoachCount = 1,
            OriginalJDVersion = originalJDVersion
        }
    };

    internal static string CreateIpk(string archiveFolder, string fileName, IReadOnlyDictionary<string, byte[]> entries)
    {
        string sourceFolder = Path.Combine(archiveFolder, ".source_" + Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(sourceFolder);
        foreach ((string relativePath, byte[] bytes) in entries)
        {
            string path = Path.Combine(sourceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? sourceFolder);
            File.WriteAllBytes(path, bytes);
        }

        Directory.CreateDirectory(archiveFolder);
        string archivePath = Path.Combine(archiveFolder, fileName);
        PackQuietly(sourceFolder, archivePath);
        Directory.Delete(sourceFolder, recursive: true);
        return archivePath;
    }

    internal static void WriteIpkHeaderValue(string archivePath, int offset, uint value)
    {
        using FileStream stream = new(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Position = offset;
        stream.Write(bytes);
    }

    internal static void PackQuietly(string sourceFolder, string archivePath)
    {
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            new UbiArtIpkWriter(sourceFolder, archivePath).Pack();
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    internal static void WriteStagedFile(string stagingFolder, string relativePath, string text)
    {
        string path = Path.Combine(stagingFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? stagingFolder);
        File.WriteAllText(path, text);
    }

    internal static string ReadIpkText(string archivePath, string relativePath)
        => Encoding.UTF8.GetString(ReadIpkBytes(archivePath, relativePath)).TrimEnd('\0');

    internal static byte[] ReadIpkBytes(string archivePath, string relativePath)
    {
        using UbiArtIpkFileSystem fileSystem = new(archivePath);
        return fileSystem.ReadAllBytes(relativePath);
    }

    internal static IReadOnlyList<string> ReadIpkEntryOrder(string archivePath)
    {
        using FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.UTF8);

        byte[] magic = reader.ReadBytes(4);
        Assert.Equal(new byte[] { 0x50, 0xEC, 0x12, 0xBA }, magic);

        _ = ReadInt32BigEndian(reader);
        _ = ReadInt32BigEndian(reader);
        _ = ReadInt32BigEndian(reader);
        int filesCount = ReadInt32BigEndian(reader);

        stream.Seek(0x30, SeekOrigin.Begin);
        List<IpkEntryStrings> entries = [];
        for (int index = 0; index < filesCount; index++)
        {
            int dummy1 = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt64BigEndian(reader);
            _ = ReadInt64BigEndian(reader);

            if (dummy1 == 2)
            {
                _ = ReadInt32BigEndian(reader);
                _ = ReadInt32BigEndian(reader);
            }

            string first = ReadLengthPrefixedString(reader);
            string second = ReadLengthPrefixedString(reader);
            _ = ReadInt32BigEndian(reader);
            _ = ReadInt32BigEndian(reader);
            entries.Add(new IpkEntryStrings(first, second));
        }

        bool swapPathAndName = entries.Count > 0 &&
            entries.Count(entry => entry.Second.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase)) > entries.Count / 2;

        return entries
            .Select(entry => ToLogicalIpkEntryPath(entry, swapPathAndName))
            .ToList();
    }

    internal static string ToLogicalIpkEntryPath(IpkEntryStrings entry, bool swapPathAndName)
    {
        (string fileName, string folderPath) = swapPathAndName
            ? (entry.First, entry.Second)
            : (entry.Second.Contains('.', StringComparison.Ordinal) ? (entry.Second, entry.First) : (entry.First, entry.Second));

        string combined = string.IsNullOrEmpty(folderPath)
            ? fileName
            : $"{folderPath.TrimEnd('/', '\\')}/{fileName}";

        return UbiArtIpkArchiveIndex.NormalizePath(combined);
    }

    internal static byte[] BuildXmlSkuScene(string mapName, string songDescPath)
    {
        string xml = $"""
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <root>
          <Scene>
            <ACTORS NAME="Actor">
              <Actor USERFRIENDLY="{mapName}" LUA="{songDescPath}">
                <COMPONENTS NAME="JD_SongDescComponent">
                  <JD_SongDescComponent />
                </COMPONENTS>
              </Actor>
            </ACTORS>
          </Scene>
          <JD_SongDatabaseSceneConfig>
            <CoverflowSkuSongs>
              <CoverflowSong name="{mapName}" cover_path="world/maps/{mapName.ToLowerInvariant()}/menuart/actors/{mapName.ToLowerInvariant()}_cover_generic.act" />
            </CoverflowSkuSongs>
          </JD_SongDatabaseSceneConfig>
        </root>
        """;

        return Encoding.UTF8.GetBytes(xml);
    }

    internal static byte[] BuildCarouselRulesJson(int originalJDVersion, string actionListName)
    {
        string json = $$"""
        {
          "__class": "GameConfig_CarouselRules",
          "actionLists": {},
          "rules": {
            "/party": {
              "__class": "CarouselRule",
              "categories": [
                {
                  "__class": "CategoryRule",
                  "act": "ui_carousel",
                  "isc": "grp_row",
                  "title": "Just Dance {{originalJDVersion}}",
                  "titleId": 12794,
                  "requests": [
                    {
                      "__class": "JD_CarouselMapRequestDesc",
                      "actionListName": "{{actionListName}}",
                      "originalJDVersion": {{originalJDVersion}},
                      "coachCount": 0,
                      "order": "title",
                      "subscribed": false,
                      "favorites": false,
                      "sweatToggleItem": false,
                      "includedTags": [ "Main" ],
                      "excludedTags": [ "KidsOnly" ]
                    },
                    {
                      "__class": "JD_CarouselMapRequestDesc",
                      "actionListName": "{{actionListName}}",
                      "originalJDVersion": {{originalJDVersion}},
                      "coachCount": 0,
                      "order": "title",
                      "subscribed": false,
                      "favorites": false,
                      "sweatToggleItem": false,
                      "includedTags": [ "Alternate" ],
                      "excludedTags": [ "KidsOnly" ]
                    }
                  ],
                  "filters": [
                    {
                      "__class": "JD_CarouselSkuFilter",
                      "gameVersion": "",
                      "platform": ""
                    }
                  ]
                },
                {
                  "__class": "CategoryRule",
                  "act": "ui_carousel",
                  "isc": "grp_row",
                  "title": "Solo",
                  "titleId": 9089,
                  "requests": [
                    {
                      "__class": "JD_CarouselMapRequestDesc",
                      "actionListName": "{{actionListName}}",
                      "originalJDVersion": 0,
                      "coachCount": 1,
                      "order": "title",
                      "subscribed": false,
                      "favorites": false,
                      "sweatToggleItem": false,
                      "includedTags": [ "Main" ],
                      "excludedTags": []
                    }
                  ]
                }
              ],
              "onlineOnly": false
            }
          }
        }
        """;

        return Encoding.UTF8.GetBytes(json);
    }

    internal static IReadOnlyList<int> ReadCarouselVersions(string archivePath, string relativePath, string route)
    {
        using JsonDocument document = JsonDocument.Parse(ReadIpkText(archivePath, relativePath));
        JsonElement categories = document.RootElement
            .GetProperty("rules")
            .GetProperty(route)
            .GetProperty("categories");

        List<int> versions = [];
        foreach (JsonElement category in categories.EnumerateArray())
        {
            if (!category.TryGetProperty("requests", out JsonElement requests))
                continue;

            foreach (JsonElement request in requests.EnumerateArray())
            {
                if (request.TryGetProperty("__class", out JsonElement className) &&
                    string.Equals(className.GetString(), "JD_CarouselMapRequestDesc", StringComparison.Ordinal) &&
                    request.TryGetProperty("originalJDVersion", out JsonElement version) &&
                    version.TryGetInt32(out int value) &&
                    value > 0)
                {
                    versions.Add(value);
                    break;
                }
            }
        }

        return versions;
    }

    internal static byte[] BuildLegacyBinarySkuSceneWithSongs(params (string MapName, string SongDescPath)[] songs)
    {
        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);

        writer.Write(1);
        writer.Write(0x00026450u);
        for (int index = 0; index < 15; index++)
            writer.Write((byte)0);
        writer.Write((byte)(songs.Length + 1));

        WriteLegacySkuActor(writer, "skuscene_db", "world/skuscenes/skuscene_base.tpl", isSongDesc: false);
        foreach ((string mapName, string songDescPath) in songs)
            WriteLegacySkuActor(writer, mapName, songDescPath, isSongDesc: true);

        WriteLegacyCoverflowFooter(writer, songs[0].MapName);
        return stream.ToArray();
    }

    internal static void WriteLegacySkuActor(
        BigEndianBinaryWriter writer,
        string name,
        string templatePath,
        bool isSongDesc)
    {
        string fileName = Path.GetFileName(templatePath.Replace('/', Path.DirectorySeparatorChar));
        string folder = templatePath[..^fileName.Length];
        uint resourceId = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(fileName));

        writer.Write(0x97CA628Bu);
        writer.Write(0);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(0);
        writer.WriteUbiArtString(name);
        writer.Write(uint.MaxValue);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(uint.MaxValue);
        writer.Write(0);
        writer.WriteUbiArtString(fileName);
        writer.WriteUbiArtString(folder);
        writer.Write(resourceId);
        writer.Write(isSongDesc ? 2 : 0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(isSongDesc ? 0xE07FCC3Fu : 0x405579FBu);
    }

    internal static void WriteLegacyCoverflowFooter(BigEndianBinaryWriter writer, string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string coverFileName = $"{mapNameLower}_cover_generic.act";
        string coverFolder = $"world/maps/{mapNameLower}/menuart/actors/";

        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0xF878DC2Du);
        writer.WriteUbiArtString("jd2020-wii-noa");
        writer.WriteUbiArtString("NCSA");
        writer.WriteUbiArtString("boot_warning_pegi.isc");
        writer.WriteUbiArtString("world/ui/screens/boot_warning/");
        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes("boot_warning_pegi.isc")));
        writer.Write(0);
        writer.Write(1);
        writer.WriteUbiArtString(mapName);
        writer.WriteUbiArtString(coverFileName);
        writer.WriteUbiArtString(coverFolder);
        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes(coverFileName)));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }

    internal static Dictionary<string, uint> ReadLegacySongDescResourceIds(byte[] bytes)
    {
        Dictionary<string, uint> ids = new(StringComparer.OrdinalIgnoreCase);
        int offset = 20;
        int actorCount = ReadInt32BigEndian(bytes, ref offset);

        for (int actorIndex = 0; actorIndex < actorCount; actorIndex++)
        {
            _ = ReadUInt32BigEndian(bytes, ref offset);
            offset += 4 * 4;
            string name = ReadUbiArtString(bytes, ref offset);
            offset += 4 * 8;
            string firstPathPart = ReadUbiArtString(bytes, ref offset);
            string secondPathPart = ReadUbiArtString(bytes, ref offset);
            uint resourceId = ReadUInt32BigEndian(bytes, ref offset);
            offset += 4 * 4;

            if (firstPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase) ||
                secondPathPart.Contains("songdesc", StringComparison.OrdinalIgnoreCase))
            {
                ids[name] = resourceId;
            }
        }

        return ids;
    }

    internal static SecureFatInfo ReadSecureFat(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.UTF8);

        uint signature = unchecked((uint)ReadInt32BigEndian(reader));
        Assert.Equal(0x55534654u, signature);

        uint engineSignature = unchecked((uint)ReadInt32BigEndian(reader));
        int version = ReadInt32BigEndian(reader);
        Assert.Equal(1, version);

        int fileIdCount = ReadInt32BigEndian(reader);
        List<SecureFatFileId> fileIds = [];
        for (int index = 0; index < fileIdCount; index++)
        {
            uint fileId = unchecked((uint)ReadInt32BigEndian(reader));
            int bundleCount = ReadInt32BigEndian(reader);
            List<byte> bundleIds = [];
            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
                bundleIds.Add(reader.ReadByte());
            fileIds.Add(new SecureFatFileId(fileId, bundleIds));
        }

        int bundleNameCount = ReadInt32BigEndian(reader);
        List<SecureFatBundle> bundles = [];
        for (int index = 0; index < bundleNameCount; index++)
        {
            byte id = reader.ReadByte();
            bundles.Add(new SecureFatBundle(id, ReadLengthPrefixedString(reader)));
        }

        return new SecureFatInfo(engineSignature, bundles, fileIds);
    }

    internal static void WriteSecureFat(
        string path,
        IReadOnlyList<SecureFatFileId> fileIds,
        IReadOnlyList<SecureFatBundle> bundles)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new(stream, Encoding.UTF8);

        writer.WriteInt32BigEndian(unchecked((int)0x55534654u));
        writer.WriteInt32BigEndian(unchecked((int)0x1D3A4C30u));
        writer.WriteInt32BigEndian(1);
        writer.WriteInt32BigEndian(fileIds.Count);

        foreach (SecureFatFileId fileId in fileIds)
        {
            writer.WriteInt32BigEndian(unchecked((int)fileId.FileId));
            writer.WriteInt32BigEndian(fileId.BundleIds.Count);
            foreach (byte bundleId in fileId.BundleIds)
                writer.Write(bundleId);
        }

        writer.WriteInt32BigEndian(bundles.Count);
        foreach (SecureFatBundle bundle in bundles)
        {
            writer.Write(bundle.Id);
            writer.WriteNTString(bundle.Name);
        }
    }

    internal static string ReadLengthPrefixedString(BinaryReader reader)
    {
        int length = ReadInt32BigEndian(reader);
        byte[] bytes = reader.ReadBytes(length);
        Assert.Equal(length, bytes.Length);
        return Encoding.UTF8.GetString(bytes);
    }

    internal static string ReadUbiArtString(byte[] bytes, ref int offset)
    {
        int length = ReadInt32BigEndian(bytes, ref offset);
        string value = Encoding.UTF8.GetString(bytes, offset, length);
        offset += length;
        return value;
    }

    internal static int ReadInt32BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    internal static long ReadInt64BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    internal static int ReadInt32BigEndian(byte[] bytes, ref int offset)
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    internal static uint ReadUInt32BigEndian(byte[] bytes, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    internal static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "jde_ipk_export_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void DeleteTempFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    internal sealed record SecureFatInfo(uint EngineSignature, IReadOnlyList<SecureFatBundle> Bundles, IReadOnlyList<SecureFatFileId> FileIds)
    {
        public IReadOnlyList<string> BundleNames => [.. Bundles.Select(bundle => bundle.Name)];
    }

    internal sealed record SecureFatFileId(uint FileId, IReadOnlyList<byte> BundleIds);

    internal sealed record SecureFatBundle(byte Id, string Name);

    internal sealed record IpkEntryStrings(string First, string Second);
}