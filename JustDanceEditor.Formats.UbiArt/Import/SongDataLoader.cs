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

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class SongDataLoader(ILogger<SongDataLoader> logger, JDI.Services.IFileSystem? io = null) : ISongDataLoader
{
    private static readonly HashSet<string> RenderOnlyCinematicClipClasses = new(StringComparer.Ordinal)
    {
        "ActorEnableClip",
        "AlphaClip",
        "ColorClip",
        "MaterialGraphicDiffuseAlphaClip",
        "MaterialGraphicDiffuseColorClip",
        "MaterialGraphicEnableLayerClip",
        "MaterialGraphicUVRotationClip",
        "MaterialGraphicUVScaleClip",
        "MaterialGraphicUVScrollClip",
        "MaterialGraphicUVTranslationClip",
        "Proportion3DClip",
        "ProportionClip",
        "RotationClip",
        "ScaleClip",
        "SecondaryTransformClip",
        "TranslationClip"
    };

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
        CookedFile musicTrackPath = GetMusicTrackPath(songData.Name, fileSystem);
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
            catch (Exception ex) when (CanSkipLegacyCommunityMashupGameplayTape(songData.Name, fileSystem, ex))
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
                // In Uncooked layout prefer .tpl first, then fallback to .dtape
                if (fileSystem.GetFilePath(danceTplRelativePath, out CookedFile? danceTplPathCooked))
                {
                    _logger.LogInformation("Loading DanceTape from .tpl (Uncooked format)");
                    using Stream danceTapeStream = fileSystem.GetFileStream(danceTplPathCooked);
                    danceTape = DeserializeDanceTapeOrSkip(danceTapeStream, danceTplRelativePath);
                }
                else if (fileSystem.GetFilePath(danceTapeRelativePath, out CookedFile? danceDtapePathCooked))
                {
                    _logger.LogInformation("DanceTape .tpl not found, falling back to .dtape");
                    using Stream danceTapeStream = fileSystem.GetFileStream(danceDtapePathCooked);
                    danceTape = DeserializeDanceTapeOrSkip(danceTapeStream, danceTapeRelativePath);
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

    private static ClipTape DeserializeClipTape(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream,
        JsonSerializerOptions options) =>
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer
            ? (ClipTape)fileSystem.VersionProfile.Serializer.Deserialize<LegacyClipTape>(stream, options)
            : fileSystem.VersionProfile.Serializer!.Deserialize<ClipTape>(stream, options);

    private static LegacyJd2014Timeline DeserializeJd2014Timeline(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream) =>
        fileSystem.VersionProfile.Serializer!.Deserialize<LegacyJd2014Timeline>(stream);

    private static LegacyBlockFlow DeserializeBlockFlow(
        JustDanceUbiArtFileSystem fileSystem,
        Stream stream) =>
        fileSystem.VersionProfile.Serializer!.Deserialize<LegacyBlockFlow>(stream);

    private void ApplyLegacyMashupIfNeeded(JDUbiArtSong songData, JustDanceUbiArtFileSystem fileSystem)
    {
        if (!fileSystem.IsLegacyMashupSelection ||
            string.IsNullOrWhiteSpace(fileSystem.LegacyMashupBaseSongName))
        {
            return;
        }

        if (fileSystem.VersionProfile.Platform == UbiArtPlatform.Uncooked ||
            fileSystem.VersionProfile.Serializer is not BinaryUbiArtSerializer)
        {
            throw new NotSupportedException("Legacy mashup blockflow export is only supported for cooked JD2014/JD2015 inputs.");
        }

        if (!fileSystem.TryGetLegacyMashupTemplatePath(fileSystem.SongName, out CookedFile? blockFlowPath))
            throw new FileNotFoundException($"Legacy mashup blockflow template not found for '{fileSystem.SongName}'.");

        using Stream blockFlowStream = fileSystem.GetFileStream(blockFlowPath);
        LegacyBlockFlow blockFlow = DeserializeBlockFlow(fileSystem, blockFlowStream);
        if (blockFlow.ComponentCount == 0 ||
            blockFlow.Component.IsMashUp == 0 ||
            blockFlow.Component.BlockDescriptorVector.Length == 0)
        {
            throw new InvalidDataException($"Legacy mashup blockflow template '{blockFlowPath.RelativePath}' does not contain any mashup blocks.");
        }

        bool isJd2014LegacyMashup = fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014;
        LegacyMashupData mashup = blockFlow.ToMashupData(fileSystem.SongName, fileSystem.LegacyMashupBaseSongName, isJd2014LegacyMashup);
        songData.LegacyMashup = mashup;
        songData.Name = fileSystem.SongName;
        songData.SongDesc.Components[0].MapName = songData.Name;
        if (isJd2014LegacyMashup)
        {
            songData.SongDesc.Components[0].NumCoach = 1;
            songData.SongDesc.Components[0].MainCoach = 0;
        }

        Structure structure = songData.MusicTrack.Components[0].TrackData.Structure;
        if (mashup.DurationBeats > 0)
            structure.EndBeat = mashup.DurationBeats;

        ApplyLegacyMashupCoachClips(songData, fileSystem, mashup, forceSingleCoachTimeline: isJd2014LegacyMashup);

        _logger.LogInformation(
            "Loaded legacy mashup blockflow '{MashupName}' over base map '{BaseSongName}' with {BlockCount} block(s), {DurationBeats} beat(s).",
            mashup.MapName,
            mashup.BaseSongName,
            mashup.Blocks.Count,
            mashup.DurationBeats);
    }

    private void ApplyLegacyMashupCoachClips(
        JDUbiArtSong songData,
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        bool forceSingleCoachTimeline)
    {
        int removed = songData.Clips.RemoveAll(IsCoachGameplayClip);
        List<Clip> remappedClips = [];
        long nextId = 1;

        foreach (LegacyMashupBlock block in mashup.Blocks)
        {
            if ((!forceSingleCoachTimeline && !block.UsesAlternativeBlock) ||
                block.DurationBeats <= 0 ||
                block.SourceBlock.IsEmptyBlock)
            {
                continue;
            }

            if (!TryLoadSourceBlockGameplayClips(fileSystem, block.SourceBlock, out IReadOnlyList<Clip> sourceClips))
            {
                _logger.LogWarning(
                    "Legacy mashup block {BlockIndex} references '{SourceSongName}', but no source timeline was found for pictograms/moves.",
                    block.Index,
                    block.SourceBlock.SongName);
                continue;
            }

            foreach (Clip sourceClip in sourceClips.Where(IsCoachGameplayClip))
            {
                if (TryRemapMashupCoachClip(sourceClip, block, nextId, forceSingleCoachTimeline, out Clip? remappedClip) &&
                    remappedClip != null)
                {
                    remappedClips.Add(remappedClip);
                    nextId++;
                }
            }
        }

        songData.Clips.AddRange(remappedClips);
        _logger.LogInformation(
            "Rebuilt legacy mashup coach timeline clips from source blocks: removed {RemovedCount}, added {AddedCount}.",
            removed,
            remappedClips.Count);
    }

    private static bool IsCoachGameplayClip(Clip clip) =>
        clip is PictogramClip or MotionClip or GoldEffectClip;

    private bool TryLoadSourceBlockGameplayClips(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupBlockDescriptor source,
        out IReadOnlyList<Clip> clips)
    {
        foreach (string candidate in EnumerateSourceTimelineCandidates(fileSystem, source).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!fileSystem.GetFilePath(candidate, out CookedFile? timelineFile))
                continue;

            try
            {
                using Stream stream = fileSystem.GetFileStream(timelineFile);
                if (Path.GetFileName(candidate).Equals("timeline.tpl", StringComparison.OrdinalIgnoreCase) &&
                    fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014)
                {
                    LegacyJd2014Timeline timeline = DeserializeJd2014Timeline(fileSystem, stream);
                    clips = [.. timeline.DanceTape.Clips];
                    return true;
                }

                JsonSerializerOptions options = new()
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new ClipConverter());
                options.Converters.Add(new IntFlexibleJsonConverter());
                options.Converters.Add(new BoolFlexibleJsonConverter());
                ClipTape tape = DeserializeClipTape(fileSystem, stream, options);
                clips = [.. ExpandClips(tape.Clips, fileSystem, options)];
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
            {
                _logger.LogDebug(ex, "Could not load legacy mashup source timeline '{TimelinePath}'.", candidate);
            }
        }

        clips = [];
        return false;
    }

    private static IEnumerable<string> EnumerateSourceTimelineCandidates(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupBlockDescriptor source)
    {
        string songName = source.SongName;
        if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014 &&
            source.DatabaseGameId is { } databaseGameId)
        {
            string databaseTimelineFolder = Path.Combine("world", "database", $"jd{databaseGameId}", songName, "timeline");
            yield return Path.Combine(databaseTimelineFolder, "timeline.tpl");
            yield return Path.Combine(databaseTimelineFolder, $"{songName}_tml_dance.tpl");
            yield return Path.Combine(databaseTimelineFolder, $"{songName}_tml_dance.dtape");
        }

        string? layoutTimelineFolder = fileSystem.VersionProfile.Layout?.GetTimelineFolder(
            fileSystem.ConversionRequest.InputPath,
            songName,
            fileSystem.VersionProfile.Platform,
            fileSystem.VersionProfile.EngineVersion);
        if (!string.IsNullOrWhiteSpace(layoutTimelineFolder))
        {
            yield return Path.Combine(layoutTimelineFolder, "timeline.tpl");
            yield return Path.Combine(layoutTimelineFolder, $"{songName}_tml_dance.tpl");
            yield return Path.Combine(layoutTimelineFolder, $"{songName}_tml_dance.dtape");
        }

        foreach (string root in new[]
        {
            Path.Combine("world", "jdblocks", songName, "timeline"),
            Path.Combine("world", "maps", songName, "timeline"),
            Path.Combine("world", "jd5", songName, "timeline"),
            Path.Combine("world", "jd2015", songName, "timeline")
        })
        {
            yield return Path.Combine(root, "timeline.tpl");
            yield return Path.Combine(root, $"{songName}_tml_dance.tpl");
            yield return Path.Combine(root, $"{songName}_tml_dance.dtape");
        }
    }

    internal static bool TryRemapMashupCoachClip(
        Clip sourceClip,
        LegacyMashupBlock block,
        long id,
        bool forceSingleCoachTimeline,
        out Clip? remappedClip)
    {
        remappedClip = null;
        double sourceStartBeat = sourceClip.StartTime / CinematicConstants.TapeFramesPerBeat;
        double sourceDurationBeats = Math.Max(0, sourceClip.Duration) / CinematicConstants.TapeFramesPerBeat;
        double sourceEndBeat = sourceDurationBeats > 0
            ? sourceStartBeat + sourceDurationBeats
            : sourceStartBeat;
        double blockStartBeat = block.SourceBlock.FirstBeat;
        double blockEndBeat = block.SourceBlock.LastBeat;

        if (sourceDurationBeats <= 0)
        {
            if (sourceStartBeat < blockStartBeat || sourceStartBeat >= blockEndBeat)
                return false;
        }
        else
        {
            if (sourceStartBeat >= blockEndBeat || sourceEndBeat <= blockStartBeat)
                return false;
        }

        double trimmedSourceStartBeat = Math.Max(sourceStartBeat, blockStartBeat);
        double trimmedSourceEndBeat = sourceDurationBeats <= 0
            ? trimmedSourceStartBeat
            : Math.Min(sourceEndBeat, blockEndBeat);
        int startTime = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(
            (float)(block.AbsoluteStartBeat + (trimmedSourceStartBeat - blockStartBeat)));
        int duration = sourceDurationBeats <= 0
            ? 0
            : Math.Max(1, LegacyGameplayBinaryHelpers.RoundBeatsToFrames((float)(trimmedSourceEndBeat - trimmedSourceStartBeat)));

        remappedClip = sourceClip switch
        {
            PictogramClip pictogram => pictogram with
            {
                Id = id,
                StartTime = startTime,
                Duration = duration
            },
            MotionClip motion => motion with
            {
                Id = id,
                StartTime = startTime,
                Duration = duration,
                CoachId = forceSingleCoachTimeline ? 0 : motion.CoachId,
                Color = motion.Color.ToArray()
            },
            GoldEffectClip gold => gold with
            {
                Id = id,
                StartTime = startTime,
                Duration = duration
            },
            _ => null
        };

        return remappedClip != null;
    }

    private static CookedFile GetMusicTrackPath(string songName, JustDanceUbiArtFileSystem fileSystem)
    {
        List<string> musicTrackSongNames = [songName];
        if (TryGetLegacyCommunityMashupBaseSongName(songName, out string? communityMashupBaseSongName))
            musicTrackSongNames.Add(communityMashupBaseSongName);

        foreach (string musicTrackSongName in musicTrackSongNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string musicTrackSongNameLower = musicTrackSongName.ToLowerInvariant();
            string audioFolder = string.Equals(musicTrackSongName, songName, StringComparison.OrdinalIgnoreCase)
                ? fileSystem.InputFolders.AudioFolder
                : Path.Combine(
                    fileSystem.VersionProfile.Layout?.GetMapWorldFolder(
                        fileSystem.ConversionRequest.InputPath,
                        musicTrackSongName,
                        fileSystem.VersionProfile.Platform,
                        fileSystem.VersionProfile.EngineVersion) ?? Path.Combine("world", "maps", musicTrackSongName),
                    "audio");
            string[] candidates =
            [
                Path.Combine(audioFolder, $"{musicTrackSongName}_musictrack.tpl"),
                Path.Combine("cache", "legacyconverteddata", musicTrackSongName, "audio", $"{musicTrackSongName}_musictrack.main_legacy.tpl"),
                Path.Combine("cache", "legacyconverteddata", musicTrackSongNameLower, "audio", $"{musicTrackSongNameLower}_musictrack.main_legacy.tpl")
            ];

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (fileSystem.GetFilePath(candidate, out CookedFile? found))
                    return found;
            }
        }

        throw new FileNotFoundException($"MusicTrack not found for '{songName}'.");
    }

    private static bool CanSkipLegacyCommunityMashupGameplayTape(
        string songName,
        JustDanceUbiArtFileSystem fileSystem,
        Exception exception) =>
        IsLegacyCommunityMashupName(songName) &&
        fileSystem.VersionProfile.Platform != UbiArtPlatform.Uncooked &&
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer &&
        exception is InvalidDataException or EndOfStreamException or IOException;

    private static bool TryGetLegacyCommunityMashupBaseSongName(string songName, [MaybeNullWhen(false)] out string baseSongName)
    {
        baseSongName = null;
        if (!IsLegacyCommunityMashupName(songName))
            return false;

        string candidate = songName[..^3];
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        baseSongName = candidate;
        return true;
    }

    private static bool IsLegacyCommunityMashupName(string songName) =>
        !string.IsNullOrWhiteSpace(songName) &&
        songName.EndsWith("CMU", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<Clip> ExpandClips(IEnumerable<Clip> clips, JustDanceUbiArtFileSystem fileSystem, JsonSerializerOptions options)
    {
        HashSet<string> recursionGuard = new(StringComparer.OrdinalIgnoreCase);
        return ExpandClipsInternal(clips, fileSystem, options, recursionGuard, 0);
    }

    private IEnumerable<Clip> ExpandClipsInternal(
        IEnumerable<Clip> clips,
        JustDanceUbiArtFileSystem fileSystem,
        JsonSerializerOptions options,
        HashSet<string> recursionGuard,
        int timeOffset)
    {
        foreach (Clip clip in clips)
        {
            if (clip is UnknownClip unknown)
            {
                if (IsRenderOnlyCinematicClipClass(unknown.OriginalClass))
                {
                    _logger.LogDebug(
                        "Ignoring render-only cinematic clip type {ClipClass} at start {StartTime}, duration {Duration}.",
                        unknown.OriginalClass,
                        unknown.StartTime,
                        unknown.Duration);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(unknown.OriginalClass))
                {
                    _logger.LogWarning(
                        "Skipping unknown clip type {ClipClass} at start {StartTime}, duration {Duration}.",
                        unknown.OriginalClass,
                        unknown.StartTime,
                        unknown.Duration);
                }
                else
                {
                    _logger.LogWarning(
                        "Skipping unknown legacy clip type 0x{ClipTypeId:X8} at start {StartTime}, duration {Duration}.",
                        unknown.TypeId,
                        unknown.StartTime,
                        unknown.Duration);
                }

                continue;
            }

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

    internal static bool IsRenderOnlyCinematicClipClass(string? className) =>
        !string.IsNullOrWhiteSpace(className) &&
        RenderOnlyCinematicClipClasses.Contains(className);

    private IEnumerable<Clip> LoadReferenceClips(
        TapeReferenceClip reference,
        JustDanceUbiArtFileSystem fileSystem,
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
                ? DeserializeClipTape(fileSystem, tapeStream, options)
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
