using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using KevInc.UbiArt.FileSystem;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupSourceVideoResolver
{
    internal static bool TryFindSourceVideo(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        LegacyMashupBlock block,
        out MashupSourceVideo sourceVideo)
    {
        LegacyMashupBlockDescriptor source = block.SourceBlock;
        sourceVideo = default;

        foreach (string candidate in EnumerateBlockVideoCandidates(fileSystem, source))
        {
            if (fileSystem.GetFilePath(candidate, out CookedFile? found))
            {
                sourceVideo = new(found, source.SongName, IsFullMapVideo: false);
                return true;
            }
        }

        foreach (string candidate in EnumerateFullMapVideoCandidates(fileSystem, source.SongName))
        {
            if (fileSystem.GetFilePath(candidate, out CookedFile? found))
            {
                sourceVideo = new(found, source.SongName, IsFullMapVideo: true);
                return true;
            }
        }

        if (TryGetNumericBlockBaseSongName(source.SongName, out string? baseSourceSongName))
        {
            foreach (string candidate in EnumerateFullMapVideoCandidates(fileSystem, baseSourceSongName))
            {
                if (fileSystem.GetFilePath(candidate, out CookedFile? found))
                {
                    sourceVideo = new(found, baseSourceSongName, IsFullMapVideo: true);
                    return true;
                }
            }
        }

        if (!block.UsesAlternativeBlock &&
            !string.Equals(source.SongName, mashup.BaseSongName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string candidate in EnumerateFullMapVideoCandidates(fileSystem, mashup.BaseSongName))
            {
                if (fileSystem.GetFilePath(candidate, out CookedFile? found))
                {
                    sourceVideo = new(found, mashup.BaseSongName, IsFullMapVideo: true);
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool TryGetNumericBlockBaseSongName(
        string songName,
        [NotNullWhen(true)] out string? baseSongName)
    {
        baseSongName = null;
        if (string.IsNullOrWhiteSpace(songName))
            return false;

        int separatorIndex = songName.LastIndexOf('_');
        if (separatorIndex <= 0 || separatorIndex == songName.Length - 1)
            return false;

        ReadOnlySpan<char> suffix = songName.AsSpan(separatorIndex + 1);
        foreach (char character in suffix)
        {
            if (!char.IsDigit(character))
                return false;
        }

        baseSongName = songName[..separatorIndex];
        return true;
    }

    internal static bool TryLoadSourceTimeline(
        JustDanceUbiArtFileSystem fileSystem,
        MashupSourceVideo sourceVideo,
        out TimelineStructureDocument? timeline)
    {
        timeline = null;

        foreach (string candidate in EnumerateSourceMusicTrackCandidates(fileSystem, sourceVideo))
        {
            if (!fileSystem.GetFilePath(candidate, out CookedFile? musicTrackPath))
                continue;

            using Stream stream = fileSystem.GetFileStream(musicTrackPath);
            MusicTrack musicTrack = fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer
                ? (MusicTrack)fileSystem.VersionProfile.Serializer.Deserialize<LegacyMusicTrack>(stream)
                : fileSystem.VersionProfile.Serializer!.Deserialize<MusicTrack>(stream);
            Structure structure = musicTrack.Components[0].TrackData.Structure;
            timeline = new TimelineStructureDocument
            {
                StartBeat = structure.StartBeat,
                EndBeat = structure.EndBeat,
                VideoStartOffset = structure.VideoStartTime,
                Markers = [.. structure.Markers]
            };
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateBlockVideoCandidates(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupBlockDescriptor source)
    {
        if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2014 &&
            source.DatabaseGameId is { } databaseGameId)
        {
            yield return Path.Combine("world", "database", $"jd{databaseGameId}", source.SongName, "videoscoach", $"{source.SongName}.webm");
        }

        yield return Path.Combine("world", "jdblocks", source.SongName, "videoscoach", $"{source.SongName}.webm");
    }

    private static IEnumerable<string> EnumerateFullMapVideoCandidates(
        JustDanceUbiArtFileSystem fileSystem,
        string songName)
    {
        string mapWorldFolder = fileSystem.VersionProfile.Layout?.GetMapWorldFolder(
            fileSystem.ConversionRequest.InputPath,
            songName,
            fileSystem.VersionProfile.Platform,
            fileSystem.VersionProfile.EngineVersion)
            ?? Path.Combine("world", "maps", songName);

        string mediaFolder = fileSystem.VersionProfile.Layout?.GetMediaFolder(
            fileSystem.ConversionRequest.InputPath,
            songName,
            fileSystem.VersionProfile.Platform,
            fileSystem.VersionProfile.EngineVersion)
            ?? Path.Combine(mapWorldFolder, "media");

        yield return Path.Combine(mapWorldFolder, "videoscoach", $"{songName}_alpha.webm");
        yield return Path.Combine(mapWorldFolder, "videoscoach", $"{songName}.webm");
        yield return Path.Combine(mediaFolder, $"{songName}.webm");
    }

    private static IEnumerable<string> EnumerateSourceMusicTrackCandidates(
        JustDanceUbiArtFileSystem fileSystem,
        MashupSourceVideo sourceVideo)
    {
        string sourceSongName = sourceVideo.SourceSongName;
        string? sourceFolder = Path.GetDirectoryName(Path.GetDirectoryName(sourceVideo.File.RelativePath));
        if (!string.IsNullOrWhiteSpace(sourceFolder))
            yield return Path.Combine(sourceFolder, "audio", $"{sourceSongName}_musictrack.tpl");

        string mapWorldFolder = fileSystem.VersionProfile.Layout?.GetMapWorldFolder(
            fileSystem.ConversionRequest.InputPath,
            sourceSongName,
            fileSystem.VersionProfile.Platform,
            fileSystem.VersionProfile.EngineVersion)
            ?? Path.Combine("world", "maps", sourceSongName);
        yield return Path.Combine(mapWorldFolder, "audio", $"{sourceSongName}_musictrack.tpl");
    }
}