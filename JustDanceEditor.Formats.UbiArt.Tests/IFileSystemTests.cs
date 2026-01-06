using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services;

using Moq;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class IFileSystemTests
{

    [Fact]
    public void EngineDetector_Uses_IFileSystem_To_Detect_Cooked_JD2015()
    {
        Mock<IFileSystem> mockFs = new();
        string root = "C:\\fake\\root2";

        // Add cooked itf_cooked folder and a nested world/jd2015 folder
        string cookedRoot = Path.Combine(root, "cache", "itf_cooked");

        mockFs.Setup(m => m.Combine(It.IsAny<string[]>())).Returns((string[] parts) => Path.Combine(parts));
        mockFs.Setup(m => m.DirectoryExists(cookedRoot)).Returns(true);
        mockFs.Setup(m => m.GetDirectories(It.IsAny<string>())).Returns<string>(path =>
        {
            if (path == cookedRoot)
                return [Path.Combine(cookedRoot, "some")];
            else if (path == Path.Combine(cookedRoot, "some"))
                return [Path.Combine(cookedRoot, "some", "world")];
            else if (path == Path.Combine(cookedRoot, "some", "world"))
                return [Path.Combine(cookedRoot, "some", "world", "jd2015")];
            else
                return [];
        });

        UbiArtEngineDetector detector = new(mockFs.Object);

        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtContainerStyle.Cooked, profile.ContainerStyle);
        Assert.Equal(UbiArtEngineVersion.JD2015, profile.EngineVersion);
    }
}