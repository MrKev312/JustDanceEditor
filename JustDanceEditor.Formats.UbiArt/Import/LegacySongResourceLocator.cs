using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import;

internal static class LegacySongResourceLocator
{
    public static CookedFile GetMusicTrackPath(string songName, JustDanceUbiArtFileSystem fileSystem)
    {
        List<string> musicTrackSongNames = [songName];
        if (TryGetCommunityMashupBaseSongName(songName, out string? baseSongName))
            musicTrackSongNames.Add(baseSongName);

        foreach (string candidateSongName in musicTrackSongNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidateSongNameLower = candidateSongName.ToLowerInvariant();
            string audioFolder = string.Equals(candidateSongName, songName, StringComparison.OrdinalIgnoreCase)
                ? fileSystem.InputFolders.AudioFolder
                : Path.Combine(
                    fileSystem.VersionProfile.Layout?.GetMapWorldFolder(
                        fileSystem.ConversionRequest.InputPath,
                        candidateSongName,
                        fileSystem.VersionProfile.Platform,
                        fileSystem.VersionProfile.EngineVersion) ?? Path.Combine("world", "maps", candidateSongName),
                    "audio");
            string[] candidates =
            [
                Path.Combine(audioFolder, $"{candidateSongName}_musictrack.tpl"),
                Path.Combine("cache", "legacyconverteddata", candidateSongName, "audio", $"{candidateSongName}_musictrack.main_legacy.tpl"),
                Path.Combine("cache", "legacyconverteddata", candidateSongNameLower, "audio", $"{candidateSongNameLower}_musictrack.main_legacy.tpl")
            ];

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (fileSystem.GetFilePath(candidate, out CookedFile? found))
                    return found;
            }
        }

        throw new FileNotFoundException($"MusicTrack not found for '{songName}'.");
    }

    public static bool CanSkipCommunityMashupGameplayTape(
        string songName,
        JustDanceUbiArtFileSystem fileSystem,
        Exception exception) =>
        IsCommunityMashupName(songName) &&
        fileSystem.VersionProfile.Platform != UbiArtPlatform.Uncooked &&
        fileSystem.VersionProfile.Serializer is BinaryUbiArtSerializer &&
        exception is InvalidDataException or EndOfStreamException or IOException;

    private static bool TryGetCommunityMashupBaseSongName(string songName, [MaybeNullWhen(false)] out string baseSongName)
    {
        baseSongName = null;
        if (!IsCommunityMashupName(songName))
            return false;

        string candidate = songName[..^3];
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        baseSongName = candidate;
        return true;
    }

    private static bool IsCommunityMashupName(string songName) =>
        !string.IsNullOrWhiteSpace(songName) && songName.EndsWith("CMU", StringComparison.OrdinalIgnoreCase);
}
