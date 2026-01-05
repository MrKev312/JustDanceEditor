using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class IFileSystemTests
{

    [Fact]
    public void LoadSongDesc_Uses_Jddb_From_IFileSystem()
    {
        // Arrange
        Mock<IFileSystem> mockFs = new();
        string root = "C:\\fake\\root";
        string jddb = Path.Combine(root, "jddb.json");

        // Simple jddb content that maps to SongDesc
        string json = "{ \"mapName\": \"mysong\", \"originalJDVersion\": 123 }";

        mockFs.Setup(m => m.Combine(It.IsAny<string[]>())).Returns((string[] parts) => Path.Combine(parts));
        mockFs.Setup(m => m.FileExists(jddb)).Returns(true);
        mockFs.Setup(m => m.ReadAllText(jddb)).Returns(json);

        SongDataLoader loader = new(NullLogger<SongDataLoader>.Instance, mockFs.Object);

        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "mysong") { Type = UbiArtType.Uncooked };
        Mock<ITempFolderManager> mockTemp = new();
        LayeredFileSystem layered = new(req, NullLogger<LayeredFileSystem>.Instance, mockFs.Object, mockTemp.Object);

        // Act
        SongDesc sd = loader.LoadSongDesc(req, layered);

        // Assert
        Assert.NotNull(sd);
        Assert.Equal("mysong", sd.COMPONENTS[0].MapName);
        Assert.Equal(123u, sd.COMPONENTS[0].OriginalJDVersion);
    }

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
            else return [];
        });

        UbiArtEngineDetector detector = new(mockFs.Object);

        UbiArtVersionProfile profile = detector.Detect(root);

        Assert.Equal(UbiArtContainerStyle.Cooked, profile.ContainerStyle);
        Assert.Equal(UbiArtEngineVersion.JD2015, profile.EngineVersion);
    }
}