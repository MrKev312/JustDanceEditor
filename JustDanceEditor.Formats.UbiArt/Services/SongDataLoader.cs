using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services;

public class SongDataLoader(ILogger<SongDataLoader> logger) : ISongDataLoader
{
    private readonly ILogger<SongDataLoader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, FileSystem fileSystem)
    {
        JDUbiArtSong songData = new();
        _logger.LogInformation("Loading song info...");

        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        _logger.LogInformation("Loading SongDesc");
        songData.SongDesc = LoadSongDesc(request, fileSystem);

        if (songData.SongDesc == null || songData.SongDesc.COMPONENTS.Length == 0)
            throw new InvalidDataException("SongDesc loaded but is invalid or empty.");

        songData.Name = songData.SongDesc.COMPONENTS[0].MapName;

        _logger.LogInformation("Loading JDVersion");
        songData.EngineVersion = songData.SongDesc.COMPONENTS[0].JDVersion;
        // Preserve original numeric version from source; normalization to consumer (Unity) is performed in the Unity pipeline
        uint originalJDVersion = songData.SongDesc.COMPONENTS[0].OriginalJDVersion;
        songData.JDVersion = originalJDVersion;
        _logger.LogInformation("Loaded engine JDVersion: {EngineVersion}, original version: {OriginalVersion}", songData.EngineVersion, songData.JDVersion);

        _logger.LogInformation("Loading MusicTrack");
        string musicTrackRelativePath = Path.Combine(fileSystem.InputFolders.AudioFolder, $"{songData.Name}_musictrack.tpl");
        CookedFile musicTrackPath = fileSystem.GetFilePath(musicTrackRelativePath);
        byte[] musicBytes = File.ReadAllBytes(musicTrackPath.FullPath);
        songData.MusicTrack = fileSystem.Serializer != null
            ? fileSystem.Serializer.Deserialize<MusicTrack>(musicBytes, options)
            : throw new InvalidOperationException("Serializer not configured on FileSystem.");

        _logger.LogInformation("Loading MainSequence");
        string mainSeqRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{songData.Name}_mainsequence.tape");
        CookedFile mainSeqPath = fileSystem.GetFilePath(mainSeqRelativePath);
        byte[] mainSeqBytes = File.ReadAllBytes(mainSeqPath.FullPath);
        ClipTape mainSequenceTape = fileSystem.Serializer != null
            ? fileSystem.Serializer.Deserialize<ClipTape>(mainSeqBytes, options)
            : throw new InvalidOperationException("Serializer not configured on FileSystem.");
        songData.Clips.AddRange(ExpandClips(mainSequenceTape.Clips, fileSystem, options));

        _logger.LogInformation("Loading DanceTape");
        string danceTapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.dtape");
        CookedFile danceTapePath = fileSystem.GetFilePath(danceTapeRelativePath);
        byte[] danceTapeBytes = File.ReadAllBytes(danceTapePath.FullPath);
        ClipTape danceTape = fileSystem.Serializer != null
            ? fileSystem.Serializer.Deserialize<ClipTape>(danceTapeBytes, options)
            : throw new InvalidOperationException("Serializer not configured on FileSystem.");
        songData.Clips.AddRange(ExpandClips(danceTape.Clips, fileSystem, options));

        string timelineIscPath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml.isc");
        CookedFile timelineFile = fileSystem.GetFilePath(timelineIscPath);

        if (ISC.GetActorPath(timelineFile, $"{songData.Name}_tml_karaoke", out string? karaokeActorRelativePath) &&
            fileSystem.GetFilePath(karaokeActorRelativePath, out CookedFile? karaokeActorFile))
        {
            // Read karaoke actor using generic serializer if possible
            byte[] karaokeActorBytes = File.ReadAllBytes(karaokeActorFile.FullPath);
            ActorTemplate karaokeActor = fileSystem.Serializer != null
                ? fileSystem.Serializer.Deserialize<ActorTemplate>(karaokeActorBytes, options)
                : JsonSerializer.Deserialize<ActorTemplate>(Encoding.UTF8.GetString(karaokeActorBytes).TrimEnd('\0'), options)!;

            if (karaokeActor.COMPONENTS.Length > 0 &&
                karaokeActor.COMPONENTS[0].TapesRack.Length > 0 &&
                karaokeActor.COMPONENTS[0].TapesRack[0].Entries.Length > 0 &&
                fileSystem.GetFilePath(karaokeActor.COMPONENTS[0].TapesRack[0].Entries[0].Path, out CookedFile? karaokeTapePathCooked))
            {
                _logger.LogInformation("Loading KaraokeTape");
                byte[] karaokeBytes = File.ReadAllBytes(karaokeTapePathCooked.FullPath);
                ClipTape karaokeTape = fileSystem.Serializer != null
                    ? fileSystem.Serializer.Deserialize<ClipTape>(karaokeBytes, options)
                    : JsonSerializer.Deserialize<ClipTape>(Encoding.UTF8.GetString(karaokeBytes).TrimEnd('\0'), options)!;
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
            }
            else
            {
                _logger.LogWarning("Karaoke tape actor structure is incomplete or tape path not found, skipping karaoke.");
            }
        }
        else
        {
            _logger.LogInformation("No karaoke tape actor found or its path is invalid, skipping karaoke.");
        }

        // Apply any data mapper transformations (for future engine-specific fixups)
        if (fileSystem.Mapper != null)
            songData = fileSystem.Mapper.Map(songData);

        return songData;
    }

    public SongDesc LoadSongDesc(UbiArtConversionRequest request, FileSystem fileSystem)
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        string songDescRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "songdesc.tpl");
        if (fileSystem.GetFilePath(songDescRelativePath, out CookedFile? songDescPathCooked))
        {
            byte[] songDescBytes = File.ReadAllBytes(songDescPathCooked.FullPath);
            return fileSystem.Serializer != null
                ? fileSystem.Serializer.Deserialize<SongDesc>(songDescBytes, options)
                : throw new InvalidOperationException("Serializer not configured on FileSystem.");
        }

        string rootJddb = Path.Combine(fileSystem.InputFolders.InputFolder, "jddb.json");
        if (File.Exists(rootJddb))
        {
            string jddbContent = File.ReadAllText(rootJddb);
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        string parentJddb = Path.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json");
        if (File.Exists(parentJddb))
        {
            string jddbContent = File.ReadAllText(parentJddb);
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        throw new FileNotFoundException("SongDesc not found (songdesc.tpl or jddb.json).");
    }

    private IEnumerable<Clip> ExpandClips(IEnumerable<Clip> clips, FileSystem fileSystem, JsonSerializerOptions options)
    {
        HashSet<string> recursionGuard = new(StringComparer.OrdinalIgnoreCase);
        return ExpandClipsInternal(clips, fileSystem, options, recursionGuard, 0);
    }

    private IEnumerable<Clip> ExpandClipsInternal(
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

    private IEnumerable<Clip> LoadReferenceClips(
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
            _logger.LogWarning("Detected recursive tape reference '{Path}', skipping to avoid infinite loop.", reference.Path);
            yield break;
        }

        try
        {
            if (!fileSystem.GetFilePath(reference.Path, out CookedFile? tapePath))
            {
                _logger.LogWarning("Referenced tape '{Path}' was not found.", reference.Path);
                yield break;
            }

            byte[] tapeBytes = File.ReadAllBytes(tapePath.FullPath);
            ClipTape tape = fileSystem.Serializer != null
                ? fileSystem.Serializer.Deserialize<ClipTape>(tapeBytes, options)
                : throw new InvalidOperationException("Serializer not configured on FileSystem.");
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