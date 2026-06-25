using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityExportNormalizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Normalize_OriginalJDVersion_To_Unity()
    {
        IntermediateSongPackage package = new() { Metadata = new IntermediateMetadata { OriginalJDVersion = 123 } };

        UnityExportData exportData = UnityExportDataBuilder.Create(package, NullLogger.Instance);
        Assert.Equal((uint)2014, exportData.Metadata.OriginalJDVersion);

        package = new() { Metadata = new IntermediateMetadata { OriginalJDVersion = 4884 } };
        exportData = UnityExportDataBuilder.Create(package, NullLogger.Instance);
        Assert.Equal((uint)2017, exportData.Metadata.OriginalJDVersion);
    }

    [Fact]
    public void ServerSongJson_Reads_Numeric_And_String_LocIds()
    {
        const string json = """
        {
            "songID": "11111111-1111-1111-1111-111111111111",
            "coachCount": 2,
            "coachNamesLocIds": [12345, "Coach Custom"],
            "danceVersionLocId": "Extreme Version"
        }
        """;

        ServerSongJSON song = JsonSerializer.Deserialize<ServerSongJSON>(json, JsonOptions)!;

        Assert.Equal(["12345", "Coach Custom"], song.CoachNamesLocIds.Select(locId => locId.Value));
        Assert.Equal("Extreme Version", song.DanceVersionLocId.Value);
    }

    [Fact]
    public void ServerSongJson_Writes_Numeric_LocIds_As_Numbers_And_Custom_LocIds_As_Strings()
    {
        ServerSongJSON song = new()
        {
            CoachNamesLocIds = ["12345", "Coach Custom"],
            DanceVersionLocId = "67890"
        };

        string json = JsonSerializer.Serialize(song, JsonOptions);

        Assert.Contains("\"coachNamesLocIds\":[12345,\"Coach Custom\"]", json);
        Assert.Contains("\"danceVersionLocId\":67890", json);
    }

    [Fact]
    public void UnityExport_Uses_Jdi_LocId_Metadata()
    {
        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata
            {
                CoachCount = 2,
                CoachNamesLocIds = ["12345", "Coach Custom"],
                DanceVersionLocId = "Extreme Version"
            }
        };

        UnityExportData exportData = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        Assert.Equal(["12345", "Coach Custom"], exportData.Metadata.CoachNamesLocIds.Select(locId => locId.Value));
        Assert.Equal("Extreme Version", exportData.Metadata.DanceVersionLocId.Value);
    }

    [Fact]
    public async Task UnitySongPreview_Loads_SongInfo_Without_MapPackage()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor.Tests", Guid.NewGuid().ToString("N"));
        string mapRoot = Path.Combine(tempRoot, "AQueda");
        string workingRoot = Path.Combine(tempRoot, "preview");

        try
        {
            Directory.CreateDirectory(mapRoot);
            await File.WriteAllTextAsync(Path.Combine(mapRoot, "SongInfo.json"), """
            {
              "songID": "64394a8a-294e-4bea-a236-ce5715ef5cca",
              "artist": "Gloria Groove",
              "coachCount": 1,
              "coachNamesLocIds": [5428],
              "credits": "Written by Gloria Groove.",
              "difficulty": 2,
              "lyricsColor": "#F38D00FF",
              "mapLength": 186.46147,
              "mapName": "AQueda",
              "originalJDVersion": 2024,
              "parentMapName": "AQueda",
              "sweatDifficulty": 2,
              "tags": ["Main"],
              "title": "A QUEDA"
            }
            """, TestContext.Current.CancellationToken);

            UnitySongPreviewProvider provider = new(NullLogger<UnitySongPreviewProvider>.Instance);

            Stopwatch stopwatch = Stopwatch.StartNew();
            SongPreviewResult result = await provider.LoadPreviewAsync(
                new SongPreviewRequest(mapRoot, workingRoot),
                TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Equal("Unity", result.FormatName);
            Assert.True(result.MaterializedRootIsTemporary);
            Assert.Equal("AQueda", result.Package.Metadata.MapName);
            Assert.Equal("A QUEDA", result.Package.Metadata.Title);
            Assert.Equal("Gloria Groove", result.Package.Metadata.Artist);
            Assert.True(result.Package.TimelineStructure.Markers.Count >= 2);
            Assert.True(File.Exists(Path.Combine(result.MaterializedRoot, "metadata.json")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}