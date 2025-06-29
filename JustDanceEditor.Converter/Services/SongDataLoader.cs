using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.UbiArt.Tapes;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

using JustDanceEditor.Logging;

using System.Text.Json;

namespace JustDanceEditor.Converter.Services;

public class SongDataLoader : ISongDataLoader
{
    public JDUbiArtSong LoadSongData(ConversionRequest request, FileSystem fileSystem)
    {
        var songData = new JDUbiArtSong();
        Logger.Log("Loading song info...");

        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        // Load SongDesc
        Logger.Log("Loading SongDesc");
        string songDescRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "songdesc.tpl");
        if (fileSystem.GetFilePath(songDescRelativePath, out CookedFile? songDescPathCooked))
        {
            songData.SongDesc = JsonSerializer.Deserialize<SongDesc>(FileSystem.ReadWithoutNull(songDescPathCooked), options)!;
        }
        else if (File.Exists(Path.Combine(fileSystem.InputFolders.InputFolder, "jddb.json")))
        {
            Logger.Log("Loading songdesc from jddb.json (root)");
            string jddbContent = File.ReadAllText(Path.Combine(fileSystem.InputFolders.InputFolder, "jddb.json"));
            // Assuming OnlineSongDesc is the structure in jddb.json
            var onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                songData.SongDesc = (SongDesc)onlineDesc;
        }
        else if (File.Exists(Path.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json")))
        {
            Logger.Log("Loading songdesc from jddb.json (parent)");
            string jddbContent = File.ReadAllText(Path.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json"));
            var onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                songData.SongDesc = (SongDesc)onlineDesc;
        }
        else
        {
            throw new FileNotFoundException("SongDesc not found (songdesc.tpl or jddb.json).");
        }

        if (songData.SongDesc == null || songData.SongDesc.COMPONENTS.Length == 0)
            throw new InvalidDataException("SongDesc loaded but is invalid or empty.");

        songData.Name = songData.SongDesc.COMPONENTS[0].MapName;

        // Load JD Version
        Logger.Log("Loading JDVersion");
        songData.EngineVersion = songData.SongDesc.COMPONENTS[0].JDVersion;
        uint originalJDVersion = songData.SongDesc.COMPONENTS[0].OriginalJDVersion;
        songData.JDVersion = originalJDVersion switch
        {
            123 => 2014,
            4884 => 2017,
            _ => originalJDVersion,
        };
        Logger.Log($"Loaded versions, engine: {songData.EngineVersion}, original version: {songData.JDVersion}");

        // Load MusicTrack
        Logger.Log("Loading MusicTrack");
        string musicTrackRelativePath = Path.Combine(fileSystem.InputFolders.AudioFolder, $"{songData.Name}_musictrack.tpl");
        CookedFile musicTrackPath = fileSystem.GetFilePath(musicTrackRelativePath);
        songData.MusicTrack = JsonSerializer.Deserialize<MusicTrack>(FileSystem.ReadWithoutNull(musicTrackPath), options)!;

        // Load MainSequence
        Logger.Log("Loading MainSequence");
        string mainSeqRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{songData.Name}_mainsequence.tape");
        CookedFile mainSeqPath = fileSystem.GetFilePath(mainSeqRelativePath);
        ClipTape mainSequenceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(mainSeqPath), options)!;
        songData.Clips.AddRange(mainSequenceTape.Clips);

        // Load DanceTape
        Logger.Log("Loading DanceTape");
        string danceTapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.dtape");
        CookedFile danceTapePath = fileSystem.GetFilePath(danceTapeRelativePath);
        ClipTape danceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(danceTapePath), options)!;
        songData.Clips.AddRange(danceTape.Clips);

        // Load KaraokeTape (if exists)
        string timelineIscPath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml.isc");
        CookedFile timelineFile = fileSystem.GetFilePath(timelineIscPath);

        if (ISC.GetActorPath(timelineFile, $"{songData.Name}_tml_karaoke", out string? karaokeActorRelativePath) &&
            fileSystem.GetFilePath(karaokeActorRelativePath, out CookedFile? karaokeActorFile))
        {
            ActorTemplate karaokeActor = JsonSerializer.Deserialize<ActorTemplate>(FileSystem.ReadWithoutNull(karaokeActorFile), options)!;
            if (karaokeActor.COMPONENTS.Length > 0 &&
                karaokeActor.COMPONENTS[0].TapesRack.Length > 0 &&
                karaokeActor.COMPONENTS[0].TapesRack[0].Entries.Length > 0 &&
                fileSystem.GetFilePath(karaokeActor.COMPONENTS[0].TapesRack[0].Entries[0].Path, out CookedFile? karaokeTapePathCooked))
            {
                Logger.Log("Loading KaraokeTape");
                ClipTape karaokeTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(karaokeTapePathCooked), options)!;
                songData.Clips.AddRange(karaokeTape.Clips);
            }
            else
            {
                Logger.Log("Karaoke tape actor structure is incomplete or tape path not found, skipping karaoke.", LogLevel.Warning);
            }
        }
        else
        {
            Logger.Log("No karaoke tape actor found or its path is invalid, skipping karaoke.", LogLevel.Important);
        }

        return songData;
    }
}