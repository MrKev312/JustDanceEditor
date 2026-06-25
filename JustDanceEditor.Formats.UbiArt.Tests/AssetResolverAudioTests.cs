using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class AssetResolverAudioTests
{
    [Fact]
    public void TryFindMainAudio_Returns_PreMergedOgg_From_MediaFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mediaFolder = Path.Combine(root, "world", "maps", "song", "media");
        Directory.CreateDirectory(mediaFolder);

        // Create a pre-merged ogg in the media folder
        string oggPath = Path.Combine(mediaFolder, "song.ogg");
        File.WriteAllText(oggPath, "OGGDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        bool found = resolver.TryFindMainAudio(new JDUbiArtSong { Name = "song" }, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        CookedFile resolvedFile = file ?? throw new System.InvalidOperationException("Expected main audio file to be resolved.");
        Assert.True(isPreMerged);
        Assert.EndsWith("song.ogg", resolvedFile.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindMainAudio_Returns_PreMergedOgg_From_MusicTrackMediaPath()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mediaFolder = Path.Combine(root, "world", "maps", "song", "media");
        Directory.CreateDirectory(mediaFolder);

        string oggPath = Path.Combine(mediaFolder, "song.ogg");
        File.WriteAllText(oggPath, "OGGDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.NX, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Cooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        JDUbiArtSong song = new()
        {
            Name = "song",
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Path = Path.Combine("world", "maps", "song", "media", "song.ogg")
                        }
                    }
                ]
            }
        };

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        bool found = resolver.TryFindMainAudio(song, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        Assert.True(isPreMerged);
        CookedFile resolvedFile = file ?? throw new System.InvalidOperationException("Expected main audio file to be resolved.");
        Assert.EndsWith("song.ogg", resolvedFile.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindMainAudio_Returns_PreMergedOgg_From_AudioFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioFolder = Path.Combine(root, "world", "maps", "song", "audio");
        Directory.CreateDirectory(audioFolder);

        File.WriteAllText(Path.Combine(audioFolder, "song.ogg"), "OGGDATA");
        File.WriteAllText(Path.Combine(audioFolder, "song_musictrack.wav"), "WAVDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.NX, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Cooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        JDUbiArtSong song = new()
        {
            Name = "song",
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Path = Path.Combine("world", "maps", "song", "audio", "song_musictrack.tpl")
                        }
                    }
                ]
            }
        };

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        bool found = resolver.TryFindMainAudio(song, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        Assert.True(isPreMerged);
        CookedFile resolvedFile = file ?? throw new System.InvalidOperationException("Expected main audio file to be resolved.");
        Assert.EndsWith("song.ogg", resolvedFile.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindAudio_Fallsback_To_Wav_Extension_If_Tpl_Not_Present()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioFolder = Path.Combine(root, "world", "maps", "song", "audio");
        Directory.CreateDirectory(audioFolder);

        // Create WAV file matching expected base name
        string baseName = "myuniquemusictrack";
        string wavPath = Path.Combine(audioFolder, baseName + ".wav");
        File.WriteAllText(wavPath, "WAVDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);

        string relativeMusicTpl = Path.Combine(fs.InputFolders.AudioFolder, baseName + ".tpl");
        bool found = resolver.TryFindAudio(relativeMusicTpl, out CookedFile? file);

        Assert.True(found);
        CookedFile resolvedFile = file ?? throw new System.InvalidOperationException("Expected audio file to be resolved.");
        Assert.EndsWith(baseName + ".wav", resolvedFile.RelativePath);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindAudio_DoesNotReturn_SoundSetTemplate_When_AudioFile_Is_Missing()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioFolder = Path.Combine(root, "world", "maps", "song", "audio", "amb");
        Directory.CreateDirectory(audioFolder);

        string baseName = "set_amb_song_intro";
        File.WriteAllText(Path.Combine(audioFolder, baseName + ".tpl"), "TPLDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song") { Type = CookedType.Uncooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new InvalidOperationException("Version profile layout was not initialized."), fs);

        string relativeMusicTpl = Path.Combine(fs.InputFolders.AudioFolder, "amb", baseName + ".tpl");
        bool found = resolver.TryFindAudio(relativeMusicTpl, out CookedFile? file);

        Assert.False(found);
        Assert.Null(file);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindAudio_EmptyPath_DoesNotReturn_InputPackage()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        string inputPackage = Path.Combine(root, "Bundle_WIIU.ipk");
        File.WriteAllText(inputPackage, "IPKDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2015, new JD2015LayoutResolver(), new BinaryUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "brokenheart") { Type = CookedType.Cooked };
        using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout, fs);
        bool found = resolver.TryFindAudio("", out CookedFile? file);

        Assert.False(found);
        Assert.Null(file);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindMainAudio_JD2015_FallsBackToSongNameInAudioFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string inputRoot = Path.Combine(root, "Bundle_WIIU");
        string audioFolder = Path.Combine(inputRoot, "cache", "itf_cooked", "wiiu", "world", "jd2015", "brokenheart", "audio");
        Directory.CreateDirectory(audioFolder);
        File.WriteAllText(Path.Combine(audioFolder, "brokenheart.wav.ckd"), "RAKIDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2015, new JD2015LayoutResolver(), new BinaryUbiArtSerializer());
        UbiArtConversionRequest req = new(inputRoot, Path.GetTempPath(), "brokenheart") { Type = CookedType.Cooked };
        using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        JDUbiArtSong song = new()
        {
            Name = "BrokenHeart",
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Path = ""
                        }
                    }
                ]
            }
        };

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout, fs);
        bool found = resolver.TryFindMainAudio(song, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        Assert.False(isPreMerged);
        CookedFile resolvedFile = file ?? throw new InvalidOperationException("Expected fallback audio file to resolve.");
        Assert.EndsWith("brokenheart.wav.ckd", resolvedFile.RelativePath, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindAudio_DlcInput_Resolves_BaseContentRoot_Audio()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string contentRoot = Path.Combine(root, "content");
        string extractedDlc = Path.Combine(contentRoot, "0011", "dlc2", "sharedaudio_main_scene_WIIU");
        string baseAudioFolder = Path.Combine(contentRoot, "Bundle_WIIU", "cache", "itf_cooked", "wiiu", "world", "jd5", "sharedaudio", "audio");
        Directory.CreateDirectory(extractedDlc);
        Directory.CreateDirectory(baseAudioFolder);

        string baseName = "sharedaudio_musictrack";
        File.WriteAllText(Path.Combine(baseAudioFolder, baseName + ".wav.ckd"), "WAVDATA");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2014, new JD2014LayoutResolver(), new BinaryUbiArtSerializer());
        UbiArtConversionRequest req = new(extractedDlc, Path.GetTempPath(), "sharedaudio") { Type = CookedType.Cooked };
        using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout, fs);
        string relativeMusicTpl = Path.Combine(fs.InputFolders.AudioFolder, baseName + ".tpl");
        bool found = resolver.TryFindAudio(relativeMusicTpl, out CookedFile? file);

        Assert.True(found);
        CookedFile resolvedFile = file ?? throw new InvalidOperationException("Expected shared base audio to be resolved.");
        Assert.EndsWith(baseName + ".wav.ckd", resolvedFile.RelativePath, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }

    [Fact]
    public void TryFindMainAudio_Durango_Returns_PreMergedOgg_From_MediaFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mediaFolder = Path.Combine(root, "world", "maps", "dancemonkey", "media");
        Directory.CreateDirectory(mediaFolder);

        File.WriteAllText(Path.Combine(mediaFolder, "audio.ogg"), "FULLSONG");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Durango, UbiArtEngineVersion.JD2021, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "dancemonkey") { Type = CookedType.Cooked };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        FileSystemAssetResolver resolver = new(fs.VersionProfile.Layout ?? throw new System.InvalidOperationException("Version profile layout was not initialized."), fs);
        JDUbiArtSong song = new()
        {
            Name = "DanceMonkey",
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Path = Path.Combine("world", "maps", "dancemonkey", "audio", "dancemonkey.wav")
                        }
                    }
                ]
            }
        };

        bool found = resolver.TryFindMainAudio(song, out CookedFile? file, out bool isPreMerged);

        Assert.True(found);
        Assert.True(isPreMerged);
        CookedFile resolvedFile = file ?? throw new System.InvalidOperationException("Expected main audio file to be resolved.");
        Assert.EndsWith("audio.ogg", resolvedFile.RelativePath, StringComparison.OrdinalIgnoreCase);

        Directory.Delete(root, true);
    }
}