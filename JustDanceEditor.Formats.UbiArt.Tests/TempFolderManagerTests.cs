using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

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
        SystemTempFolderManager manager = new();
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
            try
            {
                manager.DeleteMapFolder(mapName);
            }
            catch { }
        }
    }

    [Fact]
    public void TempFolders_Wrapper_Calls_Manager()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);

        // Replace internal temp manager with a Moq mock to verify delegation
        Mock<ITempFolderManager> mock = new();
        bool createCalled = false;
        bool deleteCalled = false;
        mock.Setup(m => m.CreateMapFolder(It.IsAny<string>())).Callback<string>(name => createCalled = true);
        mock.Setup(m => m.DeleteMapFolder(It.IsAny<string>())).Callback<string>(name => deleteCalled = true);
        fs = new JustDanceUbiArtFileSystem(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance, new SystemFileSystem(), mock.Object);

        // Ensure TempFolders delegates to ITempFolderManager
        fs.UpdateSongName("mysong");
        fs.TempFolders.CreateTempFolders();
        Assert.True(createCalled);

        fs.TempFolders.DeleteMap("mysong");
        Assert.True(deleteCalled);
    }
}