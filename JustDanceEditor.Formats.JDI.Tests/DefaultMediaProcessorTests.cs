using JustDanceEditor.Formats.JDI.Services;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class DefaultMediaProcessorTests
{
    [Fact]
    public void BuildAudioArguments_UsesFilterTrim_WhenFadesUsePreviewTimestamps()
    {
        JdiAudioEncodeRequest request = new("master.opus")
        {
            Start = TimeSpan.FromSeconds(92),
            Duration = TimeSpan.FromSeconds(30),
            FadeInDuration = TimeSpan.FromSeconds(1),
            FadeOutStart = TimeSpan.FromSeconds(29),
            FadeOutDuration = TimeSpan.FromSeconds(1)
        };

        string[] inputArguments = [.. DefaultMediaProcessor.BuildAudioInputArguments(request)];
        string[] encodeArguments = [.. DefaultMediaProcessor.BuildAudioEncodeArguments(request)];

        Assert.Empty(inputArguments);
        Assert.DoesNotContain("-ss 92", encodeArguments);
        Assert.DoesNotContain("-t 30", encodeArguments);
        Assert.Contains("-af \"atrim=start=92:duration=30,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=1,afade=t=out:st=29:d=1\"", encodeArguments);
    }

    [Fact]
    public void BuildAudioArguments_UsesValidFilterTrim_WhenPreviewStartsAtZero()
    {
        JdiAudioEncodeRequest request = new("master.opus")
        {
            Duration = TimeSpan.FromSeconds(30),
            FadeInDuration = TimeSpan.FromSeconds(1),
            FadeOutStart = TimeSpan.FromSeconds(29),
            FadeOutDuration = TimeSpan.FromSeconds(1)
        };

        string[] encodeArguments = [.. DefaultMediaProcessor.BuildAudioEncodeArguments(request)];

        Assert.Contains("-af \"atrim=duration=30,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=1,afade=t=out:st=29:d=1\"", encodeArguments);
    }

    [Fact]
    public void BuildAudioArguments_KeepsOutputSeek_WhenThereAreNoTimestampSensitiveFilters()
    {
        JdiAudioEncodeRequest request = new("master.opus")
        {
            Start = TimeSpan.FromSeconds(12),
            Duration = TimeSpan.FromSeconds(5)
        };

        string[] inputArguments = [.. DefaultMediaProcessor.BuildAudioInputArguments(request)];
        string[] encodeArguments = [.. DefaultMediaProcessor.BuildAudioEncodeArguments(request)];

        Assert.Empty(inputArguments);
        Assert.Contains("-ss 12", encodeArguments);
        Assert.Contains("-t 5", encodeArguments);
    }
}
