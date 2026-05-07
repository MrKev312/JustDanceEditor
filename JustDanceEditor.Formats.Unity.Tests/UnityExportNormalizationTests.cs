using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging.Abstractions;

using System.Linq;
using System.Text.Json;

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
}
