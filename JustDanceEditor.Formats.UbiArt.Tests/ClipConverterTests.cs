using JustDanceEditor.Formats.UbiArt.Model.Clips;

using System;
using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class ClipConverterTests
{
    [Fact]
    public void Read_UnknownJsonClip_ReturnsUnknownClipWithTiming()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new ClipConverter());

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
            options) ?? throw new InvalidOperationException("Expected a clip.");

        UnknownClip unknown = Assert.IsType<UnknownClip>(clip);
        Assert.Equal("ColorClip", unknown.OriginalClass);
        Assert.Equal(1220240912, unknown.Id);
        Assert.Equal(4100505745, unknown.TrackId);
        Assert.Equal(1, unknown.IsActive);
        Assert.Equal(12, unknown.StartTime);
        Assert.Equal(14400, unknown.Duration);
    }
}
