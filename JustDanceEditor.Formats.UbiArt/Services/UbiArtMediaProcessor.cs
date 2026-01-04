using JustDanceEditor.Formats.JDI.Services;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Services;

public sealed class UbiArtMediaProcessor : IMediaProcessor
{
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    public async Task ConvertAsync(string input, string output, string[]? extraArgs = null, CancellationToken cancellationToken = default)
    {
        IConversion conversion = FFmpeg.Conversions.New();
        // Very small wrapper - callers may do more complex operations themselves
        conversion.AddParameter($"-i \"{input}\"");
        if (extraArgs != null && extraArgs.Length > 0)
            conversion.AddParameter(string.Join(' ', extraArgs));
        conversion.SetOutput(output);
        await conversion.Start(cancellationToken);
    }
}