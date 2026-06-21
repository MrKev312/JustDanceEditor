using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicTapeClipReader
{
    internal static bool TryGetLocalTapeClips(
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache,
        string tapePath,
        ILogger logger,
        out IReadOnlyList<TapeClip> clips)
    {
        if (tapeClipCache.TryGetValue(tapePath, out clips!))
            return clips.Count > 0;

        if (!fileSystem.GetFilePath(tapePath, out CookedFile? tapeFile))
        {
            clips = [];
            tapeClipCache[tapePath] = clips;
            return false;
        }

        byte[] bytes = ReadFileBytes(fileSystem, tapeFile);
        clips = CinematicJsonTapeClipParser.LooksLikeJson(bytes)
            ? [.. CinematicJsonTapeClipParser.ReadTapeClips(bytes, tapePath, logger)]
            : [.. CinematicTapeClipParser.ReadTapeClipsSafely(bytes, 0, tapePath, logger, CreateSerializerContext(fileSystem))];
        tapeClipCache[tapePath] = clips;
        return true;
    }

    internal static IReadOnlyList<TapeClip> ReadLocalTapeClips(
        JustDanceUbiArtFileSystem fileSystem,
        string tapePath,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(tapePath);
        ArgumentNullException.ThrowIfNull(logger);

        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache = new(StringComparer.OrdinalIgnoreCase);
        return TryGetLocalTapeClips(
            fileSystem,
            tapeClipCache,
            CinematicNames.NormalizePath(tapePath),
            logger,
            out IReadOnlyList<TapeClip> clips)
            ? clips
            : [];
    }

    internal static int GetDurationFrames(
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, int> tapeDurations,
        string tapePath,
        ILogger logger)
    {
        if (tapeDurations.TryGetValue(tapePath, out int cachedDuration))
            return cachedDuration;

        if (!fileSystem.GetFilePath(tapePath, out CookedFile? tapeFile))
        {
            tapeDurations[tapePath] = 0;
            return 0;
        }

        int duration = 0;
        byte[] bytes = ReadFileBytes(fileSystem, tapeFile);
        IEnumerable<TapeClip> clips = CinematicJsonTapeClipParser.LooksLikeJson(bytes)
            ? CinematicJsonTapeClipParser.ReadTapeClips(bytes, tapePath, logger)
            : CinematicTapeClipParser.ReadTapeClipsSafely(bytes, 0, tapePath, logger, CreateSerializerContext(fileSystem));
        foreach (TapeClip childClip in clips)
            duration = Math.Max(duration, childClip.StartFrame + Math.Max(0, childClip.DurationFrames));

        tapeDurations[tapePath] = duration;
        return duration;
    }

    private static LegacyBinarySerializerContext CreateSerializerContext(JustDanceUbiArtFileSystem fileSystem)
    {
        int engineVersion = (int)fileSystem.VersionProfile.EngineVersion;
        return new LegacyBinarySerializerContext(engineVersion == 0 ? null : engineVersion);
    }

    private static byte[] ReadFileBytes(JustDanceUbiArtFileSystem fileSystem, CookedFile file)
    {
        using Stream stream = fileSystem.GetFileStream(file);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
