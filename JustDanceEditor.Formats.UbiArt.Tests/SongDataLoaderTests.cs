using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class SongDataLoaderTests
{
    [Fact]
    public void LoadSongData_Preserves_OriginalJDVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mapsFolder = Path.Combine(root, "world", "maps", "song");
        Directory.CreateDirectory(mapsFolder);

        // Also create the flat uncooked layout location (root/song/songdesc.tpl) so FileSystem direct checks succeed
        string flatSongFolder = Path.Combine(root, "song");
        Directory.CreateDirectory(flatSongFolder);

        // Create a songdesc with OriginalJDVersion = 123
        File.WriteAllText(Path.Combine(mapsFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"OriginalJDVersion\": 123, \"JDVersion\": 4884, \"MapName\": \"song\" } ] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"OriginalJDVersion\": 123, \"JDVersion\": 4884, \"MapName\": \"song\" } ] }");

        // Create audio, cinematics and timeline files that SongDataLoader expects
        Directory.CreateDirectory(Path.Combine(mapsFolder, "audio"));
        File.WriteAllText(Path.Combine(mapsFolder, "audio", "song_musictrack.tpl"), "{}");
        Directory.CreateDirectory(Path.Combine(flatSongFolder, "audio"));
        File.WriteAllText(Path.Combine(flatSongFolder, "audio", "song_musictrack.tpl"), "{}");

        Directory.CreateDirectory(Path.Combine(mapsFolder, "cinematics"));
        File.WriteAllText(Path.Combine(mapsFolder, "cinematics", "song_mainsequence.tape"), "{ \"Clips\": [] }");
        Directory.CreateDirectory(Path.Combine(mapsFolder, "timeline"));
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "song_tml_dance.dtape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "song_tml.isc"), "<Timeline><Actor USERFRIENDLY=\"song_tml_karaoke\" LUA=\"karaoke_actor.tpl\" /></Timeline>");

        Directory.CreateDirectory(Path.Combine(flatSongFolder, "cinematics"));
        File.WriteAllText(Path.Combine(flatSongFolder, "cinematics", "song_mainsequence.tape"), "{ \"Clips\": [] }");
        Directory.CreateDirectory(Path.Combine(flatSongFolder, "timeline"));
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "song_tml_dance.dtape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "song_tml.isc"), "<Timeline><Actor USERFRIENDLY=\"song_tml_karaoke\" LUA=\"karaoke_actor.tpl\" /></Timeline>");

        // Create karaoke actor and tape referenced by it
        File.WriteAllText(Path.Combine(mapsFolder, "karaoke_actor.tpl"), "{ \"COMPONENTS\": [ { \"TapesRack\": [ { \"Entries\": [ { \"Path\": \"karaoke.tape\" } ] } ] } ] }");
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "karaoke.tape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "karaoke_actor.tpl"), "{ \"COMPONENTS\": [ { \"TapesRack\": [ { \"Entries\": [ { \"Path\": \"karaoke.tape\" } ] } ] } ] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "karaoke.tape"), "{ \"Clips\": [] }");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song")
        {
            Type = CookedType.Uncooked
        };
        LayeredFileSystem fs = new(req, profile, NullLogger<LayeredFileSystem>.Instance);
        fs.Initialize();

        SongDataLoader loader = new(NullLogger<SongDataLoader>.Instance);
        JDUbiArtSong song = loader.LoadSongData(req, fs);

        Assert.Equal(123u, song.JDVersion);

        Directory.Delete(root, true);
    }
}