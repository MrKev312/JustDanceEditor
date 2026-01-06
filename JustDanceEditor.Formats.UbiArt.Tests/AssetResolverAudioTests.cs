using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Assets;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverAudioTests
{
    [Fact]
    public void TryFindMainAudio_Returns_PreMergedOgg_From_MediaFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mediaFolder = Path.Combine(root, "song", "media");
        Directory.CreateDirectory(mediaFolder);

        // Create a pre-merged ogg in the media folder
        string oggPath = Path.Combine(mediaFolder, "song.ogg");
        File.WriteAllText(oggPath, "OGGDATA");

        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = UbiArtType.Uncooked };
        LayeredFileSystem fs = new(req, NullLogger<LayeredFileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.Layout!, fs);
        bool found = resolver.TryFindMainAudio(new JDUbiArtSong { Name = "song" }, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        Assert.NotNull(file);
        Assert.True(isPreMerged);
        Assert.EndsWith("song.ogg", file!.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindAudio_Fallsback_To_Wav_Extension_If_Tpl_Not_Present()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioFolder = Path.Combine(root, "song", "audio");
        Directory.CreateDirectory(audioFolder);

        // Create WAV file matching expected base name
        string baseName = "myuniquemusictrack";
        string wavPath = Path.Combine(audioFolder, baseName + ".wav");
        File.WriteAllText(wavPath, "WAVDATA");

        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = UbiArtType.Uncooked };
        LayeredFileSystem fs = new(req, NullLogger<LayeredFileSystem>.Instance);
        fs.Configure(profile);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.Layout!, fs);

        string relativeMusicTpl = Path.Combine(fs.InputFolders.AudioFolder, baseName + ".tpl");
        bool found = resolver.TryFindAudio(relativeMusicTpl, out CookedFile? file);

        Assert.True(found);
        Assert.NotNull(file);
        Assert.EndsWith(baseName + ".wav", file!.RelativePath);

        Directory.Delete(root, true);
    }
}