using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Logging;

namespace JustDanceEditor.Formats.UbiArt.Video;

public sealed class VideoConversionOptions
{
    public string? VideoOutputFolder { get; init; }
    public string? PreviewOutputFolder { get; init; }
}

public static class VideoConverter
{
    public static Task ConvertVideoAsync(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        VideoConversionOptions? options = null,
        IVideoProgressFactory? progressFactory = null) =>
        Task.Run(() => ConvertVideo(songData, fileSystem, request, options, progressFactory));

    public static void ConvertVideo(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        VideoConversionOptions? options = null,
        IVideoProgressFactory? progressFactory = null)
    {
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            UbiArtVideoConversionRequest conversionRequest = BuildRequest(songData, fileSystem, request, options);
            IVideoProgressFactory progressFactoryToUse = progressFactory ?? new FfmpegProgressFactory();
            UbiArtVideoConverter.ConvertVideo(conversionRequest, progressFactoryToUse);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert video file: {e.Message}", LogLevel.Error);
        }
    }

    private static UbiArtVideoConversionRequest BuildRequest(
        JDUbiArtSong songData,
        FileSystem fileSystem,
        ConversionRequest request,
        VideoConversionOptions? options)
    {
        string videoOutputFolder = options?.VideoOutputFolder ?? fileSystem.OutputFolders.VideoFolder;
        string previewOutputFolder = options?.PreviewOutputFolder ?? fileSystem.OutputFolders.PreviewVideoFolder;
        string tempVideoFolder = fileSystem.TempFolders.VideoFolder;
        string sourceVideoPath = GetVideoFile(fileSystem);
        bool appendExtension = request.ExportType == ExportType.CustomServer;

        return new UbiArtVideoConversionRequest(
            songData,
            tempVideoFolder,
            videoOutputFolder,
            previewOutputFolder,
            sourceVideoPath,
            appendExtension);
    }

    private static string GetVideoFile(FileSystem fileSystem)
    {
        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out string? mediaFolder))
        {
            string[] mediaVideos = Directory.GetFiles(mediaFolder, "*.webm", SearchOption.AllDirectories);
            if (mediaVideos.Length > 0)
                return mediaVideos[0];
        }

        string videosCoachFolder = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach");
        string[] coachVideos = [.. fileSystem
            .GetAllFiles(videosCoachFolder, "*.webm")
            .Select(file => (string)file)];

        if (coachVideos.Length > 0)
            return coachVideos[0];

        throw new InvalidOperationException("No video file found.");
    }

    private sealed class FfmpegProgressFactory : IVideoProgressFactory
    {
        public IVideoConversionProgress Create(string stageName) => new ProgressAdapter(stageName);

        private sealed class ProgressAdapter(string stageName) : IVideoConversionProgress
        {
            private readonly FFMpegProgress progress = new FFMpegProgress(stageName);

            public void Update(Xabe.FFmpeg.Events.ConversionProgressEventArgs args) => progress.Update(args);

            public void Finish() => progress.Finish();
        }
    }
}
