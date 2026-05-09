using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.JDI.Services;

public sealed class DefaultMediaProcessor(IFileSystem? io = null) : IMediaProcessor
{
    private readonly IFileSystem _io = io ?? new SystemFileSystem();

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!_io.FileExists("ffmpeg.exe") && !_io.FileExists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    public async Task ConvertAsync(string input, string output, string[]? extraArgs = null, CancellationToken cancellationToken = default)
    {
        IConversion conversion = FFmpeg.Conversions.New();
        conversion.AddParameter($"-i \"{input}\"");
        if (extraArgs is { Length: > 0 })
            conversion.AddParameter(string.Join(' ', extraArgs));

        conversion.SetOutput(output);
        await conversion.Start(cancellationToken);
    }
}
