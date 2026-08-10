using JustDanceEditor.Formats.JDI.Video;

using Microsoft.Extensions.Logging;

using System.Globalization;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.JDI.Services;

public sealed class DefaultMediaProcessor(IFileSystem? io = null) : IMediaProcessor
{
    private readonly IFileSystem _io = io ?? new SystemFileSystem();

    public async Task EncodeAudioAsync(JdiAudioEncodeRequest request, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);

        IConversion conversion = FFmpeg.Conversions.New();
        conversion.SetOverwriteOutput(request.OverwriteOutput);
        AddAudioInputParameters(conversion);
        conversion.AddParameter($"-i \"{request.SourcePath}\"");
        AddAudioEncodeParameters(conversion, request);
        conversion.SetOutput(outputPath);

        await conversion.Start(cancellationToken);
    }

    public async Task<MemoryStream> EncodeAudioToMemoryAsync(JdiAudioEncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string outputFormat = request.OutputFormat ?? request.Codec ?? throw new ArgumentException("A memory audio encode needs an output format or codec.", nameof(request));

        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);

        MemoryStream output = new();
        object outputLock = new();

        IConversion conversion = FFmpeg.Conversions.New();
        AddAudioInputParameters(conversion);
        conversion.AddParameter($"-i \"{request.SourcePath}\"");
        AddAudioEncodeParameters(conversion, request);
        conversion.AddParameter($"-f {outputFormat}");
        conversion.PipeOutput(PipeDescriptor.stdout);
        conversion.OnVideoDataReceived += (_, args) =>
        {
            if (args.Data is not { Length: > 0 })
                return;

            lock (outputLock)
                output.Write(args.Data, 0, args.Data.Length);
        };

        try
        {
            await conversion.Start(cancellationToken);
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }

    public Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken cancellationToken = default)
        => JdiVideoConverter.GetOrCreateVideoAsync(request, logger, cancellationToken);

    private static void AddAudioInputParameters(IConversion conversion)
    {
        foreach (string argument in BuildAudioInputArguments())
            conversion.AddParameter(argument);
    }

    private static void AddAudioEncodeParameters(IConversion conversion, JdiAudioEncodeRequest request)
    {
        foreach (string argument in BuildAudioEncodeArguments(request))
            conversion.AddParameter(argument);
    }

    internal static IEnumerable<string> BuildAudioInputArguments()
    {
        yield break;
    }

    internal static IEnumerable<string> BuildAudioEncodeArguments(JdiAudioEncodeRequest request)
    {
        bool trimInFilter = ShouldTrimInFilter(request);

        if (request.Start > TimeSpan.Zero && !trimInFilter)
            yield return string.Create(CultureInfo.InvariantCulture, $"-ss {request.Start.TotalSeconds}");
        if (request.Duration.HasValue && !trimInFilter)
            yield return string.Create(CultureInfo.InvariantCulture, $"-t {request.Duration.Value.TotalSeconds}");
        if (!string.IsNullOrWhiteSpace(request.Codec))
            yield return $"-codec:a {ResolveAudioEncoder(request.Codec)}";
        if (request.SampleRate.HasValue)
            yield return string.Create(CultureInfo.InvariantCulture, $"-ar {request.SampleRate.Value}");
        if (request.Channels.HasValue)
            yield return string.Create(CultureInfo.InvariantCulture, $"-ac {request.Channels.Value}");
        if (!string.IsNullOrWhiteSpace(request.Bitrate))
            yield return $"-b:a {request.Bitrate}";
        if (!string.IsNullOrWhiteSpace(request.SampleFormat))
            yield return $"-sample_fmt {request.SampleFormat}";

        string filters = BuildAudioFilter(request);
        if (!string.IsNullOrWhiteSpace(filters))
            yield return $"-af \"{filters}\"";
    }

    private static string BuildAudioFilter(JdiAudioEncodeRequest request)
    {
        List<string> filters = [];
        if (ShouldTrimInFilter(request))
        {
            string trim = request.Start > TimeSpan.Zero
                ? string.Create(CultureInfo.InvariantCulture, $"atrim=start={request.Start.TotalSeconds}")
                : string.Empty;

            if (request.Duration is { } duration)
            {
                string durationOption = string.Create(CultureInfo.InvariantCulture, $"duration={duration.TotalSeconds}");
                trim = trim.Length == 0
                    ? $"atrim={durationOption}"
                    : $"{trim}:{durationOption}";
            }
            else if (trim.Length == 0)
            {
                trim = "atrim";
            }

            filters.Add(trim);
            filters.Add("asetpts=PTS-STARTPTS");
        }

        if (request.FadeInDuration is { } fadeIn && fadeIn > TimeSpan.Zero)
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"afade=t=in:st=0:d={fadeIn.TotalSeconds}"));
        if (request.FadeOutStart is { } fadeOutStart &&
            request.FadeOutDuration is { } fadeOutDuration &&
            fadeOutDuration > TimeSpan.Zero)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"afade=t=out:st={fadeOutStart.TotalSeconds}:d={fadeOutDuration.TotalSeconds}"));
        }

        return string.Join(",", filters);
    }

    private static bool ShouldTrimInFilter(JdiAudioEncodeRequest request)
        => (request.Start > TimeSpan.Zero || request.Duration.HasValue) && HasTimestampSensitiveAudioFilter(request);

    private static bool HasTimestampSensitiveAudioFilter(JdiAudioEncodeRequest request)
        => (request.FadeInDuration is { } fadeIn && fadeIn > TimeSpan.Zero) ||
           (request.FadeOutStart is not null &&
           request.FadeOutDuration is { } fadeOutDuration &&
           fadeOutDuration > TimeSpan.Zero);

    private static string ResolveAudioEncoder(string codec)
    {
        if (codec.Equals("opus", StringComparison.OrdinalIgnoreCase))
            return "libopus";
        if (codec.Equals("vorbis", StringComparison.OrdinalIgnoreCase))
            return "libvorbis";
        if (codec.Equals("mp3", StringComparison.OrdinalIgnoreCase))
            return "libmp3lame";

        return codec;
    }
}
