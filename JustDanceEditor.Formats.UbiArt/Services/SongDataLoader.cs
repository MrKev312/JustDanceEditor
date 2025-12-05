using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services;

public class SongDataLoader : ISongDataLoader
{
    public JDUbiArtSong LoadSongData(ConversionRequest request, FileSystem fileSystem)
    {
        JDUbiArtSong songData = new();
        Logger.Log("Loading song info...");

        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

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
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                songData.SongDesc = (SongDesc)onlineDesc;
        }
        else if (File.Exists(Path.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json")))
        {
            Logger.Log("Loading songdesc from jddb.json (parent)");
            string jddbContent = File.ReadAllText(Path.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json"));
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
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

        Logger.Log("Loading MusicTrack");
        string musicTrackRelativePath = Path.Combine(fileSystem.InputFolders.AudioFolder, $"{songData.Name}_musictrack.tpl");
        CookedFile musicTrackPath = fileSystem.GetFilePath(musicTrackRelativePath);
        songData.MusicTrack = JsonSerializer.Deserialize<MusicTrack>(FileSystem.ReadWithoutNull(musicTrackPath), options)!;

        Logger.Log("Loading MainSequence");
        string mainSeqRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{songData.Name}_mainsequence.tape");
        CookedFile mainSeqPath = fileSystem.GetFilePath(mainSeqRelativePath);
        ClipTape mainSequenceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(mainSeqPath), options)!;
        songData.Clips.AddRange(ExpandClips(mainSequenceTape.Clips, fileSystem, options));

        Logger.Log("Loading DanceTape");
        string danceTapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.dtape");
        CookedFile danceTapePath = fileSystem.GetFilePath(danceTapeRelativePath);
        ClipTape danceTape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(danceTapePath), options)!;
        songData.Clips.AddRange(ExpandClips(danceTape.Clips, fileSystem, options));

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
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
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

    private static IEnumerable<Clip> ExpandClips(IEnumerable<Clip> clips, FileSystem fileSystem, JsonSerializerOptions options)
    {
        HashSet<string> recursionGuard = new(StringComparer.OrdinalIgnoreCase);
        return ExpandClipsInternal(clips, fileSystem, options, recursionGuard, 0);
    }

    private static IEnumerable<Clip> ExpandClipsInternal(
        IEnumerable<Clip> clips,
        FileSystem fileSystem,
        JsonSerializerOptions options,
        HashSet<string> recursionGuard,
        int timeOffset)
    {
        foreach (Clip clip in clips)
        {
            if (clip is TapeReferenceClip reference)
            {
                foreach (Clip nested in LoadReferenceClips(reference, fileSystem, options, recursionGuard, timeOffset))
                    yield return nested;
                continue;
            }

            if (timeOffset != 0)
                clip.StartTime += timeOffset;
            yield return clip;
        }
    }

    private static IEnumerable<Clip> LoadReferenceClips(
        TapeReferenceClip reference,
        FileSystem fileSystem,
        JsonSerializerOptions options,
        HashSet<string> recursionGuard,
        int parentOffset)
    {
        if (string.IsNullOrWhiteSpace(reference.Path))
            yield break;

        string normalizedPath = reference.Path.Replace('\\', '/');
        bool added = recursionGuard.Add(normalizedPath);
        if (!added)
        {
            Logger.Log($"Detected recursive tape reference '{reference.Path}', skipping to avoid infinite loop.", LogLevel.Warning);
            yield break;
        }

        try
        {
            if (!fileSystem.GetFilePath(reference.Path, out CookedFile? tapePath))
            {
                Logger.Log($"Referenced tape '{reference.Path}' was not found.", LogLevel.Warning);
                yield break;
            }

            ClipTape tape = JsonSerializer.Deserialize<ClipTape>(FileSystem.ReadWithoutNull(tapePath), options)!;
            int offset = parentOffset + reference.StartTime;
            foreach (Clip clip in ExpandClipsInternal(tape.Clips, fileSystem, options, recursionGuard, offset))
                yield return clip;
        }
        finally
        {
            recursionGuard.Remove(normalizedPath);
        }
    }
}