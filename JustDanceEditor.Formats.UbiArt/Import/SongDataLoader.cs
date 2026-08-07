using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using NLua.Exceptions;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class SongDataLoader(ILogger<SongDataLoader> logger, JDI.Services.IFileSystem? io = null) : ISongDataLoader
{
    private readonly ILogger<SongDataLoader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem)
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

        // Preserve original numeric version from source; normalization to consumer (Unity) is performed in the Unity pipeline
        uint originalJDVersion = songData.SongDesc.Components[0].OriginalJDVersion;
        songData.JDVersion = originalJDVersion;

        _logger.LogInformation("Loaded engine JDVersion: {JDVersion}, original version: {OriginalVersion}", songData.SongDesc.Components[0].JDVersion, songData.JDVersion);

        _logger.LogInformation("Loading MusicTrack");
        CookedFile musicTrackPath = LegacySongResourceLocator.GetMusicTrackPath(songData.Name, fileSystem);
        using Stream musicStream = fileSystem.GetFileStream(musicTrackPath);

        if (fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer)
        {
            songData.MusicTrack = DeserializeMusicTrack(fileSystem, musicStream, options);
        }
        else
        {
            // For Uncooked format, check for includeReference FIRST before using regular serializer
            string musicTrackContent = new StreamReader(musicStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0');
            if (fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked && musicTrackContent.Contains("includeReference"))
            {
                _logger.LogInformation("MusicTrack contains Lua includeReference, deserializing with file system support");
                songData.MusicTrack = LuaTableSerializer.Deserialize<MusicTrack>(musicTrackContent, fileSystem);
            }
            else
            {
                // Use regular serializer for cooked format or when no includeReference
                using MemoryStream ms = new(Encoding.UTF8.GetBytes(musicTrackContent));
                songData.MusicTrack = fileSystem.VersionProfile.Serializer != null
                    ? DeserializeMusicTrack(fileSystem, ms, options)
                    : JsonSerializer.Deserialize<MusicTrack>(musicTrackContent, options) ?? throw new JsonException("Failed to deserialize MusicTrack JSON.");
            }
        }

        bool isJd2014BinaryCooked =
            fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014 &&
            fileSystem.VersionProfile.Platform != UbiArtPlatform.Uncooked &&
            fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer;
        bool isLegacyBinaryCooked =
            fileSystem.VersionProfile.Platform != UbiArtPlatform.Uncooked &&
            fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer;

        _logger.LogInformation("Loading MainSequence");
        string mainSeqRelativePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{songData.Name}_mainsequence.tape");
        if (fileSystem.GetFilePath(mainSeqRelativePath, out CookedFile? mainSeqPath))
        {
            try
            {
                using Stream mainSeqStream = fileSystem.GetFileStream(mainSeqPath);
                ClipTape mainSequenceTape = fileSystem.VersionProfile.Serializer != null
                    ? DeserializeClipTape(fileSystem, mainSeqStream, options)
                    : throw new InvalidOperationException("Serializer not configured on FileSystem.");
                songData.Clips.AddRange(ExpandClips(mainSequenceTape.Clips, fileSystem, options));
            }
            catch (Exception ex) when (isLegacyBinaryCooked && ex is InvalidDataException or EndOfStreamException or IOException)
            {
                if (fileSystem.IsLegacyMashupSelection)
                {
                    _logger.LogInformation(
                        "Skipping legacy MainSequence sound-set recovery for mashup '{SongName}' because the base map MainSequence contains non-mashup ambience clips: {Message}.",
                        fileSystem.SongName,
                        ex.Message);
                }
                else
                {
                    IReadOnlyList<SoundSetClip> soundSetClips = CinematicTapeReader.ReadSoundSetClips(fileSystem, mainSeqRelativePath, _logger);
                    songData.Clips.AddRange(soundSetClips);
                    _logger.LogWarning("Skipping legacy MainSequence tape '{Path}' while loading gameplay clips because it contains realtime cinematic clips handled during asset import: {Message}. Recovered {SoundSetClipCount} sound-set clip(s) for audio mixing.", mainSeqRelativePath, ex.Message, soundSetClips.Count);
                }
            }
        }
        else
        {
            _logger.LogInformation("MainSequence tape not found at {Path}; continuing with timeline tapes.", mainSeqRelativePath);
        }

        _logger.LogInformation("Loading DanceTape");
        string danceTapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.dtape");
        string danceTplRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_dance.tpl");
        string jd2014TimelineTplRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, "timeline.tpl");
        bool karaokeAlreadyLoaded = false;

        ClipTape DeserializeDanceTapeOrSkip(Stream danceTapeStream, string tapePath)
        {
            if (fileSystem.VersionProfile.Serializer == null)
                throw new InvalidOperationException("Serializer not configured on FileSystem.");

            try
            {
                return DeserializeClipTape(fileSystem, danceTapeStream, options);
            }
            catch (Exception ex) when (LegacySongResourceLocator.CanSkipCommunityMashupGameplayTape(songData.Name, fileSystem, ex))
            {
                _logger.LogWarning(
                    "Skipping legacy community mashup DanceTape '{TapePath}' for '{SongName}' because it contains unsupported gameplay clip data: {Message}",
                    tapePath,
                    songData.Name,
                    ex.Message);
                return new ClipTape();
            }
        }

        if (isJd2014BinaryCooked && fileSystem.GetFilePath(jd2014TimelineTplRelativePath, out CookedFile? jd2014TimelineTpl))
        {
            _logger.LogInformation("Loading JD2014 packed timeline from timeline.tpl");
            using Stream timelineStream = fileSystem.GetFileStream(jd2014TimelineTpl!);
            LegacyJd2014Timeline timeline = DeserializeJd2014Timeline(fileSystem, timelineStream);
            songData.Clips.AddRange(ExpandClips(timeline.DanceTape.Clips, fileSystem, options));
            songData.Clips.AddRange(ExpandClips(timeline.KaraokeTape.Clips, fileSystem, options));
            karaokeAlreadyLoaded = true;
        }
        else
        {
            ClipTape danceTape;

            // Prefer file type based on platform rather than file existence heuristics
            if (fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked)
            {
                CookedFile? resolvedDanceTape = null;
                string resolvedDanceTapePath = danceTapeRelativePath;

                if (fileSystem.GetFilePath(danceTplRelativePath, out CookedFile? danceTplPathCooked))
                {
                    try
                    {
                        using Stream templateStream = fileSystem.GetFileStream(danceTplPathCooked);
                        using StreamReader templateReader = new(templateStream, Encoding.UTF8);
                        string templateContent = templateReader.ReadToEnd().TrimEnd('\0');
                        string? referencedPath = LuaTableSerializer.DeserializeTapeEntryPaths(templateContent)
                            .FirstOrDefault(path => Path.GetExtension(path).Equals(".dtape", StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(referencedPath) &&
                            fileSystem.GetFilePath(referencedPath, out CookedFile? referencedDanceTape))
                        {
                            resolvedDanceTape = referencedDanceTape;
                            resolvedDanceTapePath = referencedPath;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Dance TapeCase '{TemplatePath}' did not resolve an existing .dtape; falling back to '{FallbackPath}'.",
                                danceTplRelativePath,
                                danceTapeRelativePath);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or LuaScriptException)
                    {
                        _logger.LogWarning(
                            ex,
                            "Could not resolve DanceTape from TapeCase '{TemplatePath}'; falling back to '{FallbackPath}'.",
                            danceTplRelativePath,
                            danceTapeRelativePath);
                    }
                }

                if (resolvedDanceTape is null &&
                    fileSystem.GetFilePath(danceTapeRelativePath, out CookedFile? fallbackDanceTape))
                {
                    resolvedDanceTape = fallbackDanceTape;
                }

                if (resolvedDanceTape is null)
                {
                    throw new FileNotFoundException($"Dance tape not found at {danceTplRelativePath} or {danceTapeRelativePath}");
                }

                _logger.LogInformation("Loading DanceTape from '{DanceTapePath}' resolved through the uncooked TapeCase", resolvedDanceTapePath);
                using Stream danceTapeStream = fileSystem.GetFileStream(resolvedDanceTape);
                danceTape = DeserializeDanceTapeOrSkip(danceTapeStream, resolvedDanceTapePath);
            }
            else
            {
                // In cooked layout prefer .dtape first, then fallback to .tpl
                if (fileSystem.GetFilePath(danceTapeRelativePath, out CookedFile? danceDtapePathCooked))
                {
                    using Stream danceTapeStream = fileSystem.GetFileStream(danceDtapePathCooked);
                    danceTape = DeserializeDanceTapeOrSkip(danceTapeStream, danceTapeRelativePath);
                }
                else if (fileSystem.GetFilePath(danceTplRelativePath, out CookedFile? danceTplPathCooked))
                {
                    _logger.LogInformation("Dance tape not found as .dtape, trying .tpl");
                    using Stream danceTapeStream = fileSystem.GetFileStream(danceTplPathCooked);
                    danceTape = DeserializeDanceTapeOrSkip(danceTapeStream, danceTplRelativePath);
                }
                else
                {
                    throw new FileNotFoundException($"Dance tape not found at {danceTapeRelativePath} or alternate .tpl location");
                }
            }

            songData.Clips.AddRange(ExpandClips(danceTape.Clips, fileSystem, options));
        }

        string timelineIscPath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml.isc");
        string karaokeKtapeRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_karaoke.ktape");
        string karaokeTplRelativePath = Path.Combine(fileSystem.InputFolders.TimelineFolder, $"{songData.Name}_tml_karaoke.tpl");

        if (karaokeAlreadyLoaded)
        {
            ApplyLegacyMashupIfNeeded(songData, fileSystem);
            return songData;
        }

        // Select strategy based on platform
        if (fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked)
        {
            // Prefer direct .ktape in Uncooked layout (we write .ktape when exporting Uncooked); fall back to .tpl
            if (fileSystem.GetFilePath(karaokeKtapeRelativePath, out CookedFile? karaokeKtapeFile))
            {
                _logger.LogInformation("Loading KaraokeTape from .ktape (Uncooked format)");
                using Stream karaokeStream = fileSystem.GetFileStream(karaokeKtapeFile);
                ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                    ? DeserializeClipTape(fileSystem, karaokeStream, options)
                    : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options) ?? throw new JsonException("Failed to deserialize karaoke ClipTape JSON.");
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
            }
            else if (fileSystem.GetFilePath(karaokeTplRelativePath, out CookedFile? karaokeTplFile))
            {
                _logger.LogInformation("Loading KaraokeTape from .tpl (Uncooked format fallback)");
                using Stream karaokeStream = fileSystem.GetFileStream(karaokeTplFile);
                ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                    ? DeserializeClipTape(fileSystem, karaokeStream, options)
                    : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options) ?? throw new JsonException("Failed to deserialize karaoke ClipTape JSON.");
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
            }
            else
            {
                _logger.LogInformation("No karaoke tape .ktape or .tpl found in Uncooked layout, skipping karaoke.");
            }
        }
        else
        {
            if (fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer &&
                fileSystem.GetFilePath(karaokeKtapeRelativePath, out CookedFile? directKaraokeKtapeFile))
            {
                _logger.LogInformation("Loading KaraokeTape from .ktape (legacy binary cooked format)");
                using Stream karaokeStream = fileSystem.GetFileStream(directKaraokeKtapeFile);
                ClipTape karaokeTape = DeserializeClipTape(fileSystem, karaokeStream, options);
                songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
                ApplyLegacyMashupIfNeeded(songData, fileSystem);
                return songData;
            }

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
                        : JsonSerializer.Deserialize<ActorTemplate>(new StreamReader(karaokeActorStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options) ?? throw new JsonException("Failed to deserialize karaoke actor JSON.");

                    if (karaokeActor.Components.Length > 0 &&
                        karaokeActor.Components[0].TapesRack.Length > 0 &&
                        karaokeActor.Components[0].TapesRack[0].Entries.Length > 0 &&
                        fileSystem.GetFilePath(karaokeActor.Components[0].TapesRack[0].Entries[0].Path, out CookedFile? karaokeTapePathCooked))
                    {
                        _logger.LogInformation("Loading KaraokeTape from .isc");
                        using Stream karaokeStream = fileSystem.GetFileStream(karaokeTapePathCooked);
                        ClipTape karaokeTape = fileSystem.VersionProfile.Serializer != null
                            ? DeserializeClipTape(fileSystem, karaokeStream, options)
                            : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options) ?? throw new JsonException("Failed to deserialize karaoke ClipTape JSON.");
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
                        ? DeserializeClipTape(fileSystem, karaokeStream, options)
                        : JsonSerializer.Deserialize<ClipTape>(new StreamReader(karaokeStream, Encoding.UTF8).ReadToEnd().TrimEnd('\0'), options) ?? throw new JsonException("Failed to deserialize karaoke ClipTape JSON.");
                    songData.Clips.AddRange(ExpandClips(karaokeTape.Clips, fileSystem, options));
                }
                else
                {
                    _logger.LogInformation("No karaoke tape file found (.isc or .tpl), skipping karaoke.");
                }
            }
        }

        ApplyLegacyMashupIfNeeded(songData, fileSystem);
        return songData;
    }

    public SongDesc LoadSongDesc(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new ClipConverter());
        options.Converters.Add(new IntFlexibleJsonConverter());
        options.Converters.Add(new BoolFlexibleJsonConverter());

        if (fileSystem.TryGetSongDescriptorPath(fileSystem.SongName, out CookedFile? songDescPathCooked) &&
            !songDescPathCooked.Extension.Equals(".act", StringComparison.OrdinalIgnoreCase))
        {
            using Stream songDescStream = fileSystem.GetFileStream(songDescPathCooked);
            return fileSystem.VersionProfile.Serializer != null
                ? DeserializeSongDesc(fileSystem, songDescStream, options)
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

    private static SongDesc DeserializeSongDesc(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream,
        JsonSerializerOptions options) =>
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer
            ? (SongDesc)fileSystem.VersionProfile.Serializer.Deserialize<LegacySongDesc>(stream, options)
            : fileSystem.VersionProfile.Serializer!.Deserialize<SongDesc>(stream, options);

    private static MusicTrack DeserializeMusicTrack(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream,
        JsonSerializerOptions options) =>
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer
            ? (MusicTrack)fileSystem.VersionProfile.Serializer.Deserialize<LegacyMusicTrack>(stream, options)
            : fileSystem.VersionProfile.Serializer!.Deserialize<MusicTrack>(stream, options);

    internal static ClipTape DeserializeClipTape(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream,
        JsonSerializerOptions options) =>
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer
            ? (ClipTape)fileSystem.VersionProfile.Serializer.Deserialize<LegacyClipTape>(stream, options)
            : fileSystem.VersionProfile.Serializer!.Deserialize<ClipTape>(stream, options);

    internal static LegacyJd2014Timeline DeserializeJd2014Timeline(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream) =>
        fileSystem.VersionProfile.Serializer!.Deserialize<LegacyJd2014Timeline>(stream);

    internal static LegacyBlockFlow DeserializeBlockFlow(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream) =>
        fileSystem.VersionProfile.Serializer!.Deserialize<LegacyBlockFlow>(stream);

    private void ApplyLegacyMashupIfNeeded(JDUbiArtSong songData, JustDanceUbiArtFileSystem fileSystem) =>
        new LegacyMashupApplicator(_logger).Apply(songData, fileSystem);

    internal static bool TryRemapMashupCoachClip(
        Clip sourceClip,
        LegacyMashupBlock block,
        long id,
        bool forceSingleCoachTimeline,
        out Clip? remappedClip) =>
        LegacyMashupApplicator.TryRemapMashupCoachClip(sourceClip, block, id, forceSingleCoachTimeline, out remappedClip);
    private IEnumerable<Clip> ExpandClips(
        IEnumerable<Clip> clips,
        JustDanceUbiArtFileSystem fileSystem,
        JsonSerializerOptions options) =>
        new CinematicClipReferenceExpander(_logger).Expand(clips, fileSystem, options);

    internal static bool IsRenderOnlyCinematicClipClass(string? className) =>
        CinematicClipReferenceExpander.IsRenderOnlyClipClass(className);
}
