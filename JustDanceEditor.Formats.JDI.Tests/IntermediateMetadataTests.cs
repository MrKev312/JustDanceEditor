using JustDanceEditor.Formats.JDI.Metadata;

using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class IntermediateMetadataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Metadata_Reads_Numeric_And_String_LocIds()
    {
        const string json = """
        {
            "coachCount": 2,
            "coachNamesLocIds": [12345, "Coach Custom"],
            "danceVersionLocId": "Extreme Version"
        }
        """;

        IntermediateMetadata metadata = JsonSerializer.Deserialize<IntermediateMetadata>(json, JsonOptions)!;

        Assert.Equal(["12345", "Coach Custom"], metadata.CoachNamesLocIds!.Select(locId => locId.Value));
        Assert.Equal("Extreme Version", metadata.DanceVersionLocId.Value);
    }

    [Fact]
    public void Metadata_Writes_Numeric_LocIds_As_Numbers_And_Custom_LocIds_As_Strings()
    {
        IntermediateMetadata metadata = new()
        {
            CoachCount = 2,
            CoachNamesLocIds = ["12345", "Coach Custom"],
            DanceVersionLocId = "67890"
        };

        string json = JsonSerializer.Serialize(metadata, JsonOptions);

        Assert.Contains("\"coachNamesLocIds\":[12345,\"Coach Custom\"]", json);
        Assert.Contains("\"danceVersionLocId\":67890", json);
    }
}
