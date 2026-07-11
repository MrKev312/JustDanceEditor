using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal sealed record MashupTransitionTapeTimingData(
    IReadOnlyDictionary<string, int> Durations,
    IReadOnlyDictionary<string, int> LeadFrames);

internal static class MashupTransitionTapeTiming
{
    internal static int Jd2014TransitionLeadFrames { get; } =
        (int)Math.Round(CinematicConstants.TapeFramesPerBeat);

    internal static MashupTransitionTapeTimingData Build(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        ILogger logger)
    {
        Dictionary<string, int> durations = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> leadFrames = new(StringComparer.OrdinalIgnoreCase);
        if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015)
        {
            AddTapeTiming(fileSystem, durations, leadFrames, "pulse_fg.tape", logger);
            return new MashupTransitionTapeTimingData(durations, leadFrames);
        }

        int transitionIndex = 0;
        foreach (LegacyMashupBlock block in MashupSegmentBuilder.BuildTransitionBlocks(mashup))
        {
            string coachMoveTape = $"coach_move_{(transitionIndex % 18) + 1}.tape";
            AddTapeTiming(fileSystem, durations, leadFrames, coachMoveTape, logger);
            transitionIndex++;
        }

        return new MashupTransitionTapeTimingData(durations, leadFrames);
    }

    internal static IReadOnlyDictionary<string, int> BuildUvScrollTapeDurations(
        JustDanceUbiArtFileSystem fileSystem,
        IReadOnlyList<string> uvScrollTapeNames,
        ILogger logger)
    {
        Dictionary<string, int> durations = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> leadFrames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string tapeName in uvScrollTapeNames)
            AddTapeTiming(fileSystem, durations, leadFrames, tapeName, logger);

        return durations;
    }

    internal static int? GetDuration(IReadOnlyDictionary<string, int>? durations, string tapeName) =>
        durations != null && durations.TryGetValue(tapeName, out int duration) && duration > 0
            ? duration
            : null;

    internal static int GetLead(IReadOnlyDictionary<string, int>? leadFrames, string tapeName) =>
        leadFrames != null && leadFrames.TryGetValue(tapeName, out int lead) && lead > 0
            ? lead
            : 0;

    internal static int GetLocalDurationFrames(IReadOnlyList<TapeClip> clips)
    {
        int duration = 0;
        foreach (TapeClip clip in clips)
            duration = Math.Max(duration, clip.StartFrame + Math.Max(0, clip.DurationFrames));

        return duration;
    }

    internal static int GetLocalLeadFrames(
        IReadOnlyList<TapeClip> clips,
        Func<TapeClip, bool>? predicate = null)
    {
        int lead = int.MaxValue;
        foreach (TapeClip clip in clips)
        {
            if (clip.StartFrame < 0 || Math.Max(clip.DurationFrames, 0) <= 0)
                continue;

            if (predicate != null)
            {
                if (!predicate(clip))
                    continue;
            }
            else if (clip.Targets.Count == 0)
            {
                continue;
            }

            lead = Math.Min(lead, clip.StartFrame);
        }

        return lead == int.MaxValue ? 0 : lead;
    }

    private static void AddTapeTiming(
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, int> durations,
        Dictionary<string, int> leadFrames,
        string tapeName,
        ILogger logger)
    {
        if (durations.ContainsKey(tapeName))
            return;

        string tapePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", tapeName);
        IReadOnlyList<TapeClip> clips = CinematicTapeClipReader.ReadLocalTapeClips(fileSystem, tapePath, logger);
        int duration = GetLocalDurationFrames(clips);
        if (duration > 0)
            durations[tapeName] = duration;

        int lead = GetLocalLeadFrames(clips);
        if (lead > 0)
            leadFrames[tapeName] = lead;
    }
}
