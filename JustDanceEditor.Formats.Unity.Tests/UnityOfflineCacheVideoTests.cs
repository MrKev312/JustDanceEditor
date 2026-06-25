using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.Unity.Converters;

using System;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityOfflineCacheVideoTests
{
    [Theory]
    [InlineData("vp8")]
    [InlineData("vp9")]
    public void OfflineCacheVideoCompatibility_AllowsSixteenNineVp8OrVp9Webm(string codec)
    {
        JdiVideoInfo info = new(1920, 1080, TimeSpan.FromSeconds(30), codec);

        Assert.True(IntermediateToUnityConverter.IsOfflineCacheCompatibleVideo("video.webm", info));
    }

    [Theory]
    [InlineData("video.mp4", 1920, 1080, "vp9")]
    [InlineData("video.webm", 1440, 1080, "vp9")]
    [InlineData("video.webm", 1920, 1080, "h264")]
    public void OfflineCacheVideoCompatibility_RejectsAnythingOutsideTheRuntimeRequirements(
        string path,
        int width,
        int height,
        string codec)
    {
        JdiVideoInfo info = new(width, height, TimeSpan.FromSeconds(30), codec);

        Assert.False(IntermediateToUnityConverter.IsOfflineCacheCompatibleVideo(path, info));
    }

    [Fact]
    public void OfflineCacheVideoCompatibility_CropsNonSixteenNineSources()
    {
        JdiVideoTransform transform = IntermediateToUnityConverter.BuildSixteenNineTransform(
            new JdiVideoInfo(1440, 1080, TimeSpan.FromSeconds(30), "vp9"));

        Assert.NotEmpty(transform.AdditionalFilters);
    }
}