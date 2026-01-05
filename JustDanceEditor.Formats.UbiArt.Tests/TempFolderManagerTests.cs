using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.JDI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;
using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class TempFolderManagerTests
{
    [Fact]
    public void SystemTempFolderManager_Creates_And_Deletes_MapFolder()
    {
        var manager = new SystemTempFolderManager();
        string mapName = Path.GetRandomFileName();

        try
        {
            manager.CreateMapFolder(mapName);
            string mapFolder = manager.GetMapFolder(mapName);
            string audioFolder = manager.GetAudioFolder(mapName);

            Assert.True(Directory.Exists(mapFolder));
            Assert.True(Directory.Exists(audioFolder));

            manager.DeleteMapFolder(mapName);
            Assert.False(Directory.Exists(mapFolder));
        }
        finally
        {
            // Best-effort cleanup
            try { manager.DeleteMapFolder(mapName); } catch { }
        }
    }

    [Fact]
    public void TempFolders_Wrapper_Calls_Manager()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = UbiArtType.Uncooked };
        LayeredFileSystem fs = new(req, NullLogger<LayeredFileSystem>.Instance);

        // Replace internal temp manager with a Moq mock to verify delegation
        var mock = new Mock<ITempFolderManager>();
        bool createCalled = false;
        bool deleteCalled = false;
        mock.Setup(m => m.CreateMapFolder(It.IsAny<string>())).Callback<string>(name => createCalled = true);
        mock.Setup(m => m.DeleteMapFolder(It.IsAny<string>())).Callback<string>(name => deleteCalled = true);
        fs = new LayeredFileSystem(req, NullLogger<LayeredFileSystem>.Instance, new SystemFileSystem(), mock.Object);

        // Ensure TempFolders delegates to ITempFolderManager
        fs.UpdateSongName("mysong");
        fs.TempFolders.CreateTempFolders();
        Assert.True(createCalled);

        fs.TempFolders.DeleteMap("mysong");
        Assert.True(deleteCalled);
    }
}