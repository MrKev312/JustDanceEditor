using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

internal sealed class LegacyMashupApplicator(ILogger logger)
{
    public void Apply(JDUbiArtSong songData, JustDanceUbiArtFileSystem fileSystem)
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
        LegacyBlockFlow blockFlow = SongDataLoader.DeserializeBlockFlow(fileSystem, blockFlowStream);
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
    
        logger.LogInformation(
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
                logger.LogWarning(
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
        logger.LogInformation(
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
                    LegacyJd2014Timeline timeline = SongDataLoader.DeserializeJd2014Timeline(fileSystem, stream);
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
                ClipTape tape = SongDataLoader.DeserializeClipTape(fileSystem, stream, options);
                clips = [.. new CinematicClipReferenceExpander(logger).Expand(tape.Clips, fileSystem, options)];
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
            {
                logger.LogDebug(ex, "Could not load legacy mashup source timeline '{TimelinePath}'.", candidate);
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
    
    public static bool TryRemapMashupCoachClip(
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
                Color = [.. motion.Color]
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
}

