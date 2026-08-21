using JustDanceEditor.Formats.UbiArt.Import;

using System;
using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class UbiArtInputLocationTests
{
    [Fact]
    public void Resolve_UncookedDirectMap_ReturnsContentRootAndSongName()
    {
        string root = CreateTempDirectory();
        try
        {
            string mapFolder = Path.Combine(root, "world", "maps", "SayMyName");
            Directory.CreateDirectory(mapFolder);
            File.WriteAllText(Path.Combine(mapFolder, "SongDesc.tpl"), "params = {}");

            UbiArtInputLocation input = UbiArtInputLocation.Resolve(mapFolder);

            Assert.True(input.IsDirectMap);
            Assert.Equal(Path.GetFullPath(root), input.RootPath);
            Assert.Equal("SayMyName", input.SongName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_CookedDirectMap_ReturnsRootAboveCache()
    {
        string root = CreateTempDirectory();
        try
        {
            string mapFolder = Path.Combine(root, "cache", "itf_cooked", "nx", "world", "maps", "Song");
            Directory.CreateDirectory(mapFolder);
            File.WriteAllText(Path.Combine(mapFolder, "songdesc.tpl.ckd"), "{}");

            UbiArtInputLocation input = UbiArtInputLocation.Resolve(mapFolder, "song");

            Assert.True(input.IsDirectMap);
            Assert.Equal(Path.GetFullPath(root), input.RootPath);
            Assert.Equal("Song", input.SongName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "JustDanceEditor.UbiArt.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
