using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Serialization.Legacy;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.Serialization.Legacy;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicTapeReader
{
    public static CinematicTapeData ReadCinematicTapes(
        JustDanceUbiArtFileSystem fileSystem,
        double videoDurationSeconds,
        ILogger logger,
        IReadOnlyList<TapeVisit>? additionalRootVisits = null,
        CinematicScene? scene = null)
    {
        IReadOnlyDictionary<string, CinematicTapeCaseDefinition> tapeCasesByActorKey =
            CinematicTapeLauncherPlanner.BuildTapeCaseDefinitions(fileSystem, scene, logger);
        ClipTargetResolver? targetResolver = scene == null ? null : ClipTargetResolver.Create(scene);
        Dictionary<string, int> sequenceIndices = new(StringComparer.OrdinalIgnoreCase);
        TapeVisit[] primaryRoots =
        [
            .. EnumerateMainSequenceTapePaths(fileSystem)
                .Select(path => new TapeVisit(CinematicNames.NormalizePath(path), 0))
        ];

        return CinematicTapeGraphReader.Read(
            fileSystem,
            primaryRoots,
            additionalRootVisits ?? [],
            videoDurationSeconds,
            (parentVisit, clip) => CinematicTapeLauncherPlanner.GetLauncherVisits(
                fileSystem,
                tapeCasesByActorKey,
                targetResolver,
                sequenceIndices,
                parentVisit,
                clip,
                logger),
            logger);
    }

    private static IEnumerable<string> EnumerateMainSequenceTapePaths(JustDanceUbiArtFileSystem fileSystem)
    {
        foreach (string mapWorldFolder in EnumerateMapWorldFolderPathVariants(fileSystem.InputFolders.MapWorldFolder))
        {
            foreach (string songName in EnumerateSongNamePathVariants(fileSystem.SongName))
                yield return Path.Combine(mapWorldFolder, "cinematics", $"{songName}_mainsequence.tape");
        }

        if (string.Equals(fileSystem.SongName, "_mashup", StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", "mu_mainsequence.tape");
    }

    private static IEnumerable<string> EnumerateMapWorldFolderPathVariants(string mapWorldFolder)
    {
        if (string.IsNullOrWhiteSpace(mapWorldFolder))
            yield break;

        yield return mapWorldFolder;
        string? parent = Path.GetDirectoryName(mapWorldFolder);
        string folderName = Path.GetFileName(mapWorldFolder);
        string lowerFolderName = folderName.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(parent) &&
            !string.Equals(folderName, lowerFolderName, StringComparison.Ordinal))
        {
            yield return Path.Combine(parent, lowerFolderName);
        }
    }

    private static IEnumerable<string> EnumerateSongNamePathVariants(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            yield break;

        yield return songName;
        string lower = songName.ToLowerInvariant();
        if (!string.Equals(songName, lower, StringComparison.Ordinal))
            yield return lower;
    }

    public static IReadOnlyList<SoundSetClip> ReadSoundSetClips(
        JustDanceUbiArtFileSystem fileSystem,
        string mainSequencePath,
        ILogger logger)
    {
        return CinematicTapeTraversal.ReadReferencedClips(
                fileSystem,
                new TapeVisit(CinematicNames.NormalizePath(mainSequencePath), 0),
                logger)
            .Where(clip =>
                clip.TypeId == CinematicClipIds.SoundSet &&
                !string.IsNullOrWhiteSpace(clip.Path))
            .Select(clip => new SoundSetClip
            {
                StartTime = clip.StartFrame,
                Duration = clip.DurationFrames,
                SoundSetPath = clip.Path!
            })
            .ToArray();
    }
}
