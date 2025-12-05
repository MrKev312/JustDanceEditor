using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

namespace JustDanceEditor.Formats.UbiArt.Audio;

public sealed class AudioConversionOptions
{
    public required string MasterOutputFolder { get; init; }
    public required string PreviewOutputFolder { get; init; }
}

public static class AudioConverter
{
    private static readonly IAudioConverter audioConverter = new VGMStreamAdapter();

    public static Task ConvertAudioAsync(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        AudioConversionOptions options) =>
        Task.Run(() => ConvertAudio(songData, fileSystem, request, options));

    public static void ConvertAudio(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        AudioConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            UbiArtAudioConversionRequest conversionRequest = BuildRequest(songData, fileSystem, request, options);
            UbiArtAudioConverter.ConvertAudio(conversionRequest);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert audio files: {e.Message}", LogLevel.Error);
        }
    }

    private static UbiArtAudioConversionRequest BuildRequest(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        AudioConversionOptions options)
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
            audioConverter);
    }

    private static IReadOnlyList<UbiArtAudioClipSource> BuildClipSources(JDUbiArtSong songData, FileSystem fileSystem)
    {
        SoundSetClip[] audioClips = [.. songData.Clips.OfType<SoundSetClip>()];
        List<UbiArtAudioClipSource> clipSources = new(audioClips.Length);

        foreach (SoundSetClip clip in audioClips)
        {
            string relativePath = Path.ChangeExtension(clip.SoundSetPath, ".wav");
            if (!fileSystem.GetFilePath(relativePath, out CookedFile? wavPath))
                continue;

            clipSources.Add(new UbiArtAudioClipSource(clip, wavPath));
        }

        return clipSources;
    }

    private static CookedFile GetMainSongPath(JDUbiArtSong songData, FileSystem fileSystem, out bool isPreMerged)
    {
        isPreMerged = false;

        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out string? mediaFolder))
        {
            string[] oggFiles = Directory.GetFiles(mediaFolder, "*.ogg", SearchOption.AllDirectories);
            if (oggFiles.Length > 0)
            {
                isPreMerged = true;
                return new CookedFile(oggFiles.First());
            }
        }

        string relativePath = songData.MusicTrack.COMPONENTS[0].trackData.path;

        if (fileSystem.GetFilePath(relativePath, out CookedFile? mainSongPath))
            return mainSongPath;

        throw new InvalidOperationException("Main song not found.");
    }
}