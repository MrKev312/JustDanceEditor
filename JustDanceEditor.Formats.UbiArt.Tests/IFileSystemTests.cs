using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

using Moq;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class IFileSystemTests
{

    [Theory]
    [InlineData("wiiu", UbiArtPlatform.Cafe)]
    [InlineData("nx", UbiArtPlatform.NX)]
    [InlineData("pc", UbiArtPlatform.Win32)]
    [InlineData("x360", UbiArtPlatform.Xenon)]
    [InlineData("durango", UbiArtPlatform.Durango)]
    [InlineData("orbis", UbiArtPlatform.Orbis)]
    public void EngineDetector_Detects_Cooked_JD2015_Platforms(string platformFolder, UbiArtPlatform expectedPlatform)
    {
        Mock<IUbiArtFileSystem> mockFs = new();
        string root = "C:\\fake\\root3";

        string cookedRoot = Path.Combine(root, "cache", "itf_cooked");

        mockFs.Setup(m => m.Combine(It.IsAny<string[]>())).Returns((string[] parts) => Path.Combine(parts));
        mockFs.Setup(m => m.DirectoryExists(cookedRoot)).Returns(true);
        mockFs.Setup(m => m.DirectoryExists(Path.Combine(cookedRoot, platformFolder, "world", "jd2015"))).Returns(true);
        mockFs.Setup(m => m.GetDirectories(It.IsAny<string>())).Returns<string>(path =>
        {
            if (path == cookedRoot)
                return [Path.Combine(cookedRoot, platformFolder)];
            else if (path == Path.Combine(cookedRoot, platformFolder))
                return [Path.Combine(cookedRoot, platformFolder, "world")];
            else if (path == Path.Combine(cookedRoot, platformFolder, "world"))
                return [Path.Combine(cookedRoot, platformFolder, "world", "jd2015")];
            else
                return [];
        });

        UbiArtEngineDetector detector = new(mockFs.Object);

        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(expectedPlatform, profile.Platform);
        Assert.Equal(UbiArtEngineVersion.JD2015, profile.EngineVersion);
        Assert.IsType<JD2015LayoutResolver>(profile.Layout);
    }
}