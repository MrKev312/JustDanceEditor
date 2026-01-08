using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Services;

public class SongDataLoader(ILogger<SongDataLoader> logger, JDI.Services.IFileSystem? io = null) : ISongDataLoader
{
    private readonly ILogger<SongDataLoader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, LayeredFileSystem fileSystem)
    {
        JDUbiArtSong songData = new();
        _logger.LogInformation("Loading song info...");

        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        _logger.LogInformation("Loading SongDesc");
        songData.SongDesc = LoadSongDesc(request, fileSystem);

        if (songData.SongDesc == null || songData.SongDesc.Components.Length == 0)
            throw new InvalidDataException("SongDesc loaded but is invalid or empty.");

        songData.Name = songData.SongDesc.Components[0].MapName;

        _logger.LogInformation("Loading JDVersion");
        songData.EngineVersion = songData.SongDesc.Components[0].JDVersion;
        // Preserve original numeric version from source; normalization to consumer (Unity) is performed in the Unity pipeline
        uint originalJDVersion = songData.SongDesc.Components[0].OriginalJDVersion;
        songData.JDVersion = originalJDVersion;

        // Propagate the numeric JD version into the LayeredFileSystem's profile so downstream
        // components (e.g., pictogram sorting) can select the correct behaviour.
        try
        {
            fileSystem.SetEngineNumericVersion(originalJDVersion);
        }
        catch
        {
            // Non-fatal: if the filesystem isn't in a state to accept updates, log and continue.
            _logger.LogDebug("Could not set EngineNumericVersion on FileSystem; continuing without numeric version.");
        }

        _logger.LogInformation("Loaded engine JDVersion: {EngineVersion}, original version: {OriginalVersion}", songData.EngineVersion, songData.JDVersion);

        _logger.LogInformation("Loading MusicTrack");
        string musicTrackRelativePath = Path.Combine(fileSystem.InputFolders.AudioFolder, $"{songData.Name}_musictrack.tpl");
        CookedFile musicTrackPath = fileSystem.GetFilePath(musicTrackRelativePath);
        using Stream musicStream = fileSystem.GetFileStream(musicTrackPath);
        
        // For Uncooked format, check for includeReference FIRST before using regular serializer
        string musicTrackContent = new StreamReader(musicStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0');
        if (fileSystem.VersionProfile.ContainerStyle == UbiArtContainerStyle.Uncooked && musicTrackContent.Contains("includeReference"))
        {
            _logger.LogInformation("MusicTrack contains Lua includeReference, deserializing with file system support");
            songData.MusicTrack = LuaTableSerializer.Deserialize<MusicTrack>(musicTrackContent, fileSystem);
        }
        else
        {
            // Use regular serializer for cooked format or when no includeReference
            using MemoryStream ms = new(Encoding.UTF8.GetBytes(musicTrackContent));
            songData.MusicTrack = fileSystem.VersionProfile.Serializer != null
                ? fileSystem.VersionProfile.Serializer.Deserialize<MusicTrack>(ms, options)
                : JsonSerializer.Deserialize<MusicTrack>(musicTrackContent, options)!;
        }

        _logger.LogInformation("Loading MainSequence");
        string mainSeqRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{songData.Name}_mainsequence.tape");
        CookedFile mainSeqPath = fileSystem.GetFilePath(mainSeqRelativePath);
        using Stream mainSeqStream = fileSystem.GetFileStream(mainSeqPath);
        ClipTape mainSequenceTape = fileSystem.VersionProfile.Serializer != null
            ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(mainSeqStream, options)
            : throw new InvalidOperationException("Serializer not configured on FileSystem.");
        songData.Clips.AddRange(ExpandClips(mainSequenceTape.Clips, fileSystem, options));

        _logger.LogInformation("Loading DanceTape");
        string danceTapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.dtape");
        string danceTplRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.tpl");

        ClipTape danceTape;

        // Prefer file type based on container style rather than file existence heuristics
        if (fileSystem.VersionProfile.ContainerStyle == UbiArtContainerStyle.Uncooked)
        {
            // In Uncooked layout prefer .tpl first, then fallback to .dtape
            if (fileSystem.GetFilePath(danceTplRelativePath, out CookedFile? danceTplPathCooked))
            {
                _logger.LogInformation("Loading DanceTape from .tpl (Uncooked format)");
                using Stream danceTapeStream = fileSystem.GetFileStream(danceTplPathCooked);
                danceTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(danceTapeStream, options)
                    : throw new InvalidOperationException("Serializer not configured on FileSystem.");
            }
            else if (fileSystem.GetFilePath(danceTapeRelativePath, out CookedFile? danceDtapePathCooked))
            {
                _logger.LogInformation("DanceTape .tpl not found, falling back to .dtape");
                using Stream danceTapeStream = fileSystem.GetFileStream(danceDtapePathCooked);
                danceTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(danceTapeStream, options)
                    : throw new InvalidOperationException("Serializer not configured on FileSystem.");
            }
            else
            {
                throw new FileNotFoundException($"Dance tape not found at {danceTplRelativePath} or {danceTapeRelativePath}");
            }
        }
        else
        {
            // In cooked layout prefer .dtape first, then fallback to .tpl
            if (fileSystem.GetFilePath(danceTapeRelativePath, out CookedFile? danceDtapePathCooked))
            {
                using Stream danceTapeStream = fileSystem.GetFileStream(danceDtapePathCooked);
                danceTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(danceTapeStream, options)
                    : throw new InvalidOperationException("Serializer not configured on FileSystem.");
            }
            else if (fileSystem.GetFilePath(danceTplRelativePath, out CookedFile? danceTplPathCooked))
            {
                _logger.LogInformation("Dance tape not found as .dtape, trying .tpl");
                using Stream danceTapeStream = fileSystem.GetFileStream(danceTplPathCooked);
                danceTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(danceTapeStream, options)
                    : throw new InvalidOperationException("Serializer not configured on FileSystem.");
            }
            else
            {
                throw new FileNotFoundException($"Dance tape not found at {danceTapeRelativePath} or alternate .tpl location");
            }
        }

        songData.Clips.AddRange(ExpandClips(danceTape.Clips, fileSystem, options));

        string timelineIscPath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml.isc");
        string karaokeKtapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_karaoke.ktape");
        string karaokeTplRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_karaoke.tpl");

        // Select strategy based on container style
        if (fileSystem.VersionProfile.ContainerStyle == UbiArtContainerStyle.Uncooked)
        {
            // Prefer direct .ktape in Uncooked layout (we write .ktape when exporting Uncooked); fall back to .tpl
            if (fileSystem.GetFilePath(karaokeKtapeRelativePath, out CookedFile? karaokeKtapeFile))
            {
                _logger.LogInformation("Loading KaraokeTape from .ktape (Uncooked format)");
                using Stream karaokeStream = fileSystem.GetFileStream(karaokeKtapeFile);
                ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(karaokeStream, options)
                    : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options)!;
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
            }
            else if (fileSystem.GetFilePath(karaokeTplRelativePath, out CookedFile? karaokeTplFile))
            {
                _logger.LogInformation("Loading KaraokeTape from .tpl (Uncooked format fallback)");
                using Stream karaokeStream = fileSystem.GetFileStream(karaokeTplFile);
                ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                    ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(karaokeStream, options)
                    : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options)!;
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
            }
            else
            {
                _logger.LogInformation("No karaoke tape .ktape or .tpl found in Uncooked layout, skipping karaoke.");
            }
        }
        else
        {
            // Cooked layout: expect an .isc descriptor referencing the karaoke actor/tape
            if (fileSystem.GetFilePath(timelineIscPath, out CookedFile? timelineFileResult))
            {
                CookedFile timelineFile = timelineFileResult;

                if (ISC.GetActorPath(timelineFile, $"{songData.Name}_tml_karaoke", out string? karaokeActorRelativePath, fileSystem) &&
                    fileSystem.GetFilePath(karaokeActorRelativePath, out CookedFile? karaokeActorFile))
                {
                    // Read karaoke actor using generic serializer if possible
                    using Stream karaokeActorStream = fileSystem.GetFileStream(karaokeActorFile);
                    ActorTemplate karaokeActor = fileSystem.VersionProfile.Serializer != null
                        ? fileSystem.VersionProfile.Serializer.Deserialize<ActorTemplate>(karaokeActorStream, options)
                        : JsonSerializer.Deserialize<ActorTemplate>(new StreamReader(karaokeActorStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options)!;

                    if (karaokeActor.Components.Length > 0 &&
                        karaokeActor.Components[0].TapesRack.Length > 0 &&
                        karaokeActor.Components[0].TapesRack[0].Entries.Length > 0 &&
                        fileSystem.GetFilePath(karaokeActor.Components[0].TapesRack[0].Entries[0].Path, out CookedFile? karaokeTapePathCooked))
                    {
                        _logger.LogInformation("Loading KaraokeTape from .isc");
                        using Stream karaokeStream = fileSystem.GetFileStream(karaokeTapePathCooked);
                        ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                            ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(karaokeStream, options)
                            : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options)!;
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
            }
            else
            {
                _logger.LogInformation("No karaoke tape .isc found, checking for a .tpl fallback");
                if (fileSystem.GetFilePath(karaokeTplRelativePath, out CookedFile? karaokeTplFile))
                {
                    _logger.LogInformation("Loading KaraokeTape from .tpl fallback");
                    using Stream karaokeStream = fileSystem.GetFileStream(karaokeTplFile);
                    ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                        ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(karaokeStream, options)
                        : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options)!;
                    songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
                }
                else
                {
                    _logger.LogInformation("No karaoke tape file found (.isc or .tpl), skipping karaoke.");
                }
            }
        }

        // Apply any data mapper transformations (for future engine-specific fixups)
        if (fileSystem.VersionProfile.Mapper != null)
            songData = fileSystem.VersionProfile.Mapper.Map(songData);

        return songData;
    }

    public SongDesc LoadSongDesc(UbiArtConversionRequest request, LayeredFileSystem fileSystem)
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        string songDescRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "songdesc.tpl");
        if (fileSystem.GetFilePath(songDescRelativePath, out CookedFile? songDescPathCooked))
        {
            using Stream songDescStream = fileSystem.GetFileStream(songDescPathCooked);
            return fileSystem.VersionProfile.Serializer != null
                ? fileSystem.VersionProfile.Serializer.Deserialize<SongDesc>(songDescStream, options)
                : throw new InvalidOperationException("Serializer not configured on FileSystem.");
        }

        // Prefer stream-based access via FileSystem for jddb.json when available
        if (fileSystem.GetFilePath("jddb.json", out CookedFile? rootJddbPath))
        {
            using Stream s = fileSystem.GetFileStream(rootJddbPath);
            string jddbContent = new StreamReader(s, Encoding.UTF8).ReadToEnd().TrimEnd('\0');
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        // Try parent folder via relative lookup
        if (fileSystem.GetFilePath(Path.Combine("..", "jddb.json"), out CookedFile? parentJddbPath))
        {
            using Stream s = fileSystem.GetFileStream(parentJddbPath);
            string jddbContent = new StreamReader(s, Encoding.UTF8).ReadToEnd().TrimEnd('\0');
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        // Fallback to existing behavior (direct IFileSystem access)
        string rootJddb = _io.Combine(fileSystem.InputFolders.InputFolder, "jddb.json");
        if (_io.FileExists(rootJddb))
        {
            string jddbContent = _io.ReadAllText(rootJddb);
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        string parentJddb = _io.Combine(fileSystem.InputFolders.InputFolder, "..", "jddb.json");
        if (_io.FileExists(parentJddb))
        {
            string jddbContent = _io.ReadAllText(parentJddb);
            OnlineSongDesc? onlineDesc = JsonSerializer.Deserialize<OnlineSongDesc>(jddbContent, options);
            if (onlineDesc != null)
                return (SongDesc)onlineDesc;
        }

        throw new FileNotFoundException("SongDesc not found (songdesc.tpl or jddb.json).");
    }

    private IEnumerable<Clip> ExpandClips(IEnumerable<Clip> clips, LayeredFileSystem fileSystem, JsonSerializerOptions options)
    {
        HashSet<string> recursionGuard = new(StringComparer.OrdinalIgnoreCase);
        return ExpandClipsInternal(clips, fileSystem, options, recursionGuard, 0);
    }

    private IEnumerable<Clip> ExpandClipsInternal(
        IEnumerable<Clip> clips,
        LayeredFileSystem fileSystem,
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
        LayeredFileSystem fileSystem,
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

            using Stream tapeStream = fileSystem.GetFileStream(tapePath);
            ClipTape tape = fileSystem.VersionProfile.Serializer != null
                ? fileSystem.VersionProfile.Serializer.Deserialize<ClipTape>(tapeStream, options)
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