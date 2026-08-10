using JustDanceEditor.Formats.UbiArt.Model.Clips;

using System;
using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class ClipConverterTests
{
    private static readonly JsonSerializerOptions ClipJsonOptions = CreateClipJsonOptions();

    private static JsonSerializerOptions CreateClipJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new ClipConverter());
        return options;
    }

    [Fact]
    public void Read_UnknownJsonClip_ReturnsUnknownClipWithTiming()
    {
        Clip clip = JsonSerializer.Deserialize<Clip>(
            """
            {
              "__class": "ColorClip",
              "Id": 1220240912,
              "TrackId": 4100505745,
              "IsActive": 1,
              "StartTime": 12,
              "Duration": 14400
            }
            """,
            ClipJsonOptions) ?? throw new InvalidOperationException("Expected a clip.");

        UnknownClip unknown = Assert.IsType<UnknownClip>(clip);
        Assert.Equal("ColorClip", unknown.OriginalClass);
        Assert.Equal(1220240912, unknown.Id);
        Assert.Equal(4100505745, unknown.TrackId);
        Assert.Equal(1, unknown.IsActive);
        Assert.Equal(12, unknown.StartTime);
        Assert.Equal(14400, unknown.Duration);
    }
}
