using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtLayoutTests
{
    [Fact]
    public void Layout_Should_Handle_Uncooked_JD2014_Symmetry()
    {
        UbiArtLayoutResolver layout = new();
        UbiArtContainerStyle style = UbiArtContainerStyle.Uncooked;
        UbiArtEngineVersion version = UbiArtEngineVersion.JD2014;

        var path = layout.GetMapWorldFolder("/root", "SongName", style, version);
        // Should be world/maps/jd5/SongName
        Assert.Contains("jd5", path);
    }

    [Fact]
    public async Task ExportSymmetry_Uncooked_Map()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        UbiArtLayoutResolver layout = new();
        UbiArtContainerStyle style = UbiArtContainerStyle.Uncooked;
        UbiArtEngineVersion version = UbiArtEngineVersion.JD2014;
        string mapWorldRelative = layout.GetMapWorldFolder(root, "song", style, version);
        string mapWorldFolder = Path.Combine(root, mapWorldRelative);

        // create expected folders
        Directory.CreateDirectory(mapWorldFolder);
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "audio"));
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "timeline"));
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "cinematics"));

        IntermediateSongPackage package = new() { Metadata = new JDI.Metadata.IntermediateMetadata { MapName = "song" } };

        await UbiArtAssetWriter.ExportToUncookedAsync(package, null, root, NullLogger.Instance, layout, style, version);

        // Check songdesc is written under the map world folder
        Assert.True(File.Exists(Path.Combine(mapWorldFolder, "songdesc.tpl")));
        // Check timeline file written
        string timelineFolder = Path.Combine(root, layout.GetTimelineFolder(root, "song", style, version));
        Assert.True(File.Exists(Path.Combine(timelineFolder, "song_tml_dance.dtape")));

        Directory.Delete(root, true);
    }
}