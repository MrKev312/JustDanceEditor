using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI.Conversion;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityConversionStrategyTests
{
    [Fact]
    public void CustomServerTarget_IsListedUnderPc()
    {
        UnityConversionStrategy strategy = new();

        ConversionTargetDefinition target = Assert.Single(strategy.GetExportTargets(), target => target.TargetCode == "unity-custom-server");

        Assert.Equal("unity-custom-server", target.TargetCode);
        Assert.Equal("pc", target.Platform.PlatformCode);
        Assert.Equal("PC", target.Platform.DisplayName);
        Assert.Equal("JDNext Server", target.DisplayName);
    }

    [Fact]
    public void OfflineCacheTarget_IsListedUnderNx()
    {
        UnityConversionStrategy strategy = new();

        ConversionTargetDefinition target = Assert.Single(strategy.GetExportTargets(), target => target.TargetCode == "unity-offline-cache");

        Assert.Equal("switch", target.Platform.PlatformCode);
        Assert.Equal("NX (Nintendo Switch)", target.Platform.DisplayName);
        Assert.Equal("JD2023+ (Unity)", target.DisplayName);
    }

    [Fact]
    public void OfflineCacheRequest_UsesCacheNumberAndHeadlessCacheOverride()
    {
        UnityConversionStrategy strategy = new();
        ConversionTargetDefinition target = Assert.Single(strategy.GetExportTargets(), target => target.TargetCode == "unity-offline-cache");
        PromptAnswerSet answers = new();
        answers.Set("unity.templatePath", "C:\\Template");
        answers.Set("unity.cacheNumber", "7");
        answers.Set(UnityPromptIds.GenerateCacheIfMissing, "true");

        UnityConversionRequest request = Assert.IsType<UnityConversionRequest>(strategy.CreateExportRequest(new ConversionRequestContext(
            "C:\\Input",
            "C:\\Output",
            Target: target,
            Answers: answers)));

        Assert.Equal(ExportType.OfflineCache, request.ExportType);
        Assert.Equal("C:\\Input", request.InputPath);
        Assert.Equal("C:\\Output", request.OutputPath);
        Assert.Equal(7u, request.CacheNumber);
        Assert.True(request.GenerateCacheIfMissing);
    }

    [Fact]
    public void CustomServerPlatforms_ArePcNxPs5AndXboxScarlett()
    {
        UnityServerPlatform[] platforms = [.. UnityServerPlatforms.CustomServerDefaults];

        Assert.Collection(
            platforms,
            platform =>
            {
                Assert.Equal("PC", platform.FolderName);
                Assert.Equal(5u, platform.BuildTarget);
            },
            platform =>
            {
                Assert.Equal("NX", platform.FolderName);
                Assert.Equal(38u, platform.BuildTarget);
            },
            platform =>
            {
                Assert.Equal("PS5", platform.FolderName);
                Assert.Equal(44u, platform.BuildTarget);
            },
            platform =>
            {
                Assert.Equal("XboxScarlett", platform.FolderName);
                Assert.Equal(42u, platform.BuildTarget);
            });
    }

    [Fact]
    public void ImportSourcePriority_PrefersNxBundles()
    {
        UnityServerPlatform[] platforms = [.. UnityServerPlatforms.ImportSourcePriority];

        Assert.Collection(
            platforms,
            platform => Assert.Equal("NX", platform.FolderName),
            platform => Assert.Equal("PC", platform.FolderName),
            platform => Assert.Equal("PS5", platform.FolderName),
            platform => Assert.Equal("XboxScarlett", platform.FolderName));
    }
}
