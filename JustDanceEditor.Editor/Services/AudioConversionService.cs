using JustDanceEditor.Formats.JDI.Video;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Audio conversion and analysis utilities backed by FFmpeg.
/// Handles format conversion, duration extraction, and waveform data generation.
/// </summary>
public class AudioConversionService
{
    /// <summary>
    /// Gets the duration of an audio file using FFmpeg (works with all formats including Opus).
    /// </summary>
    public static async Task<double> GetDurationAsync(string audioPath)
    {
        if (!File.Exists(audioPath))
            return 0;

        await JdiFfmpegResolver.GetFfmpegPathAsync();
        IMediaInfo info = await FFmpeg.GetMediaInfo(audioPath);
        IAudioStream? audioStream = info.AudioStreams.FirstOrDefault();
        return audioStream?.Duration.TotalSeconds ?? info.Duration.TotalSeconds;
    }

    public static async Task<PcmWaveAudioData> DecodeToPcmAsync(
        string audioPath,
        int sampleRate = 48000,
        int channels = 2,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("The audio file does not exist.", audioPath);

        byte[] bytes = await RunFfmpegToMemoryAsync(
            audioPath,
            [
                "-vn",
                "-sn",
                "-ar",
                sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-ac",
                channels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-sample_fmt",
                "s16",
                "-acodec",
                "pcm_s16le",
                "-f",
                "s16le",
                "pipe:1"
            ],
            cancellationToken);

        return PcmWaveAudioData.FromS16Le(sampleRate, channels, bytes);
    }

    /// <summary>
    /// Extracts normalized float waveform samples from an audio file through raw PCM.
    /// Returns an empty array if the file does not exist.
    /// </summary>
    public static async Task<float[]> GetWaveformDataAsync(string audioPath)
    {
        if (!File.Exists(audioPath))
            return [];

        byte[] bytes = await RunFfmpegToMemoryAsync(
            audioPath,
            [
                "-f",
                "s16le",
                "-acodec",
                "pcm_s16le",
                "-ac",
                "1",
                "-ar",
                "8000",
                "pipe:1"
            ]);

        float[] samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;

        return samples;
    }

    private static async Task<byte[]> RunFfmpegToMemoryAsync(
        string inputPath,
        IReadOnlyList<string> outputArguments,
        CancellationToken cancellationToken = default)
    {
        string ffmpegPath = await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);

        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        foreach (string argument in outputArguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start FFmpeg audio decoder.");

        await using MemoryStream output = new();
        Task copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        Task<string> readError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await Task.WhenAll(copyOutput, readError, process.WaitForExitAsync(cancellationToken));
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw;
        }

        string error = await readError;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg audio decode failed with code {process.ExitCode}: {error}");

        return output.ToArray();
    }
}
