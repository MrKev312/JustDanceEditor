using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Audio;

public sealed class AudioConversionOptions
{
    public required string MasterOutputFolder { get; init; }
    public required string PreviewOutputFolder { get; init; }
}

public static class AudioConverter
{
    public static Task ConvertAudioAsync(
        JDUbiArtSong songData,
        JustDanceUbiArtFileSystem fileSystem,
        AudioConversionOptions options,
        IAudioConverter audioConverter,
        ILogger logger) =>
        Task.Run(() => ConvertAudio(songData, fileSystem, options, audioConverter, logger));

    public static void ConvertAudio(
        JDUbiArtSong songData,
        JustDanceUbiArtFileSystem fileSystem,
        AudioConversionOptions options,
        IAudioConverter audioConverter,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(audioConverter);

        try
        {
            UbiArtAudioConversionRequest conversionRequest = BuildRequest(songData, fileSystem, options, audioConverter);
            UbiArtAudioConverter.ConvertAudio(conversionRequest, logger);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to convert audio files: {Message}", e.Message);
        }
    }

    private static UbiArtAudioConversionRequest BuildRequest(
        JDUbiArtSong songData,
        JustDanceUbiArtFileSystem fileSystem,
        AudioConversionOptions options,
        IAudioConverter audioConverter)
    {
        string masterOutputFolder = options.MasterOutputFolder;
        string previewOutputFolder = options.PreviewOutputFolder;
        string tempAudioFolder = fileSystem.TempFolders.AudioFolder;

        IReadOnlyList<UbiArtAudioClipSource> clipSources = BuildClipSources(songData, fileSystem);

        CookedFile mainSongPath = GetMainSongPath(songData, fileSystem, out bool isPreMerged);

        return new UbiArtAudioConversionRequest(
            songData,
            mainSongPath,
            clipSources,
            tempAudioFolder,
            masterOutputFolder,
            previewOutputFolder,
            isPreMerged,
            audioConverter,
            fileSystem);
    }

    private static IReadOnlyList<UbiArtAudioClipSource> BuildClipSources(JDUbiArtSong songData, JustDanceUbiArtFileSystem fileSystem)
    {
        SoundSetClip[] audioClips = [.. songData.Clips.OfType<SoundSetClip>()];
        List<UbiArtAudioClipSource> clipSources = new(audioClips.Length);

        foreach (SoundSetClip clip in audioClips)
        {
            CookedFile? audioPath = null;
            foreach (string relativePath in UbiArtSoundSetTemplateResolver.GetAudioPathCandidates(fileSystem, clip.SoundSetPath))
            {
                bool found = fileSystem.AssetResolver?.TryFindAudio(relativePath, out audioPath) == true ||
                    fileSystem.GetFilePath(relativePath, out audioPath);
                if (found && audioPath != null && IsSupportedAudioFile(audioPath))
                    break;

                audioPath = null;
            }

            if (audioPath == null)
                continue;

            clipSources.Add(new UbiArtAudioClipSource(clip, audioPath));
        }

        return clipSources;
    }

    private static bool IsSupportedAudioFile(CookedFile file) =>
        file.Extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".opus", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".wem", StringComparison.OrdinalIgnoreCase);

    private static CookedFile GetMainSongPath(JDUbiArtSong songData, JustDanceUbiArtFileSystem fileSystem, out bool isPreMerged)
    {
        isPreMerged = false;

        if (fileSystem.AssetResolver != null && fileSystem.AssetResolver.TryFindMainAudio(songData, out CookedFile? found, out bool merged))
        {
            isPreMerged = merged;
            return found;
        }

        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out _))
        {
            CookedFile[] oggFiles = fileSystem.GetAllFiles(fileSystem.InputFolders.MediaFolder, "*.ogg");
            if (oggFiles.Length > 0)
            {
                isPreMerged = true;
                return oggFiles[0];
            }
        }

        string relativePath = songData.MusicTrack.Components[0].TrackData.Path;

        if (!string.IsNullOrWhiteSpace(relativePath) && fileSystem.GetFilePath(relativePath, out CookedFile? mainSongPath))
        {
            isPreMerged = IsPreMergedAudioFile(mainSongPath);
            return mainSongPath;
        }

        string songName = string.IsNullOrWhiteSpace(songData.Name)
            ? fileSystem.SongName
            : songData.Name;
        if (!string.IsNullOrWhiteSpace(songName) &&
            fileSystem.GetFilePath(Path.Combine(fileSystem.InputFolders.AudioFolder, songName + ".wav"), out mainSongPath))
        {
            return mainSongPath;
        }

        throw new InvalidOperationException("Main song not found.");
    }

    private static bool IsPreMergedAudioFile(CookedFile file)
    {
        string relativePath = file.RelativePath.Replace('\\', '/');
        return relativePath.Contains("/media/", StringComparison.OrdinalIgnoreCase);
    }
}