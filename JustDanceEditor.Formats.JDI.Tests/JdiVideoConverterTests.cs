using JustDanceEditor.Formats.JDI.Video;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class JdiVideoConverterTests
{
    [Fact]
    public void BuildVideoMuxerArgs_MovesWebmCuesToFront()
    {
        string args = JdiVideoConverter.BuildVideoMuxerArgs(@"C:\temp\preview.webm");

        Assert.Equal("-f webm -cues_to_front 1 ", args);
    }

    [Fact]
    public void BuildVideoMuxerArgs_DoesNotForceNonWebmContainers()
    {
        string args = JdiVideoConverter.BuildVideoMuxerArgs(@"C:\temp\preview.mp4");

        Assert.Empty(args);
    }
}