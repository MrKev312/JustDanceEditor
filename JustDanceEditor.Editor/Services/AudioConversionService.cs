using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Audio conversion and analysis utilities backed by FFmpeg.
/// Handles format conversion (any → WAV), duration extraction, and waveform data generation.
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

        IMediaInfo info = await FFmpeg.GetMediaInfo(audioPath);
        IAudioStream? audioStream = info.AudioStreams.FirstOrDefault();
        return audioStream?.Duration.TotalSeconds ?? info.Duration.TotalSeconds;
    }

    /// <summary>
    /// Converts any audio file to WAV using FFmpeg (for NAudio playback).
    /// Returns the path to the temp WAV file.
    /// </summary>
    public static async Task<string> ConvertToWavAsync(string audioPath)
    {
        string tempWav = Path.Combine(Path.GetTempPath(), $"jdi_preview_{Guid.NewGuid()}.wav");
        IConversion conversion = FFmpeg.Conversions.New();
        conversion.AddParameter($"-y -i \"{audioPath}\" -ar 48000 -ac 2 -sample_fmt s16");
        conversion.SetOutput(tempWav);
        conversion.SetOverwriteOutput(true);
        await conversion.Start();
        return tempWav;
    }

    /// <summary>
    /// Extracts normalized float waveform samples from an audio file (via FFmpeg → raw PCM).
    /// Returns an empty array if the file does not exist.
    /// </summary>
    public static async Task<float[]> GetWaveformDataAsync(string audioPath)
    {
        if (!File.Exists(audioPath))
            return [];

        string tempOut = Path.Combine(Path.GetTempPath(), $"jdi_wave_{Guid.NewGuid()}.raw");
        try
        {
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.AddParameter($"-y -i \"{audioPath}\" -f s16le -ac 1 -ar 8000");
            conversion.SetOutput(tempOut);
            conversion.SetOverwriteOutput(true);
            await conversion.Start();

            byte[] bytes = await File.ReadAllBytesAsync(tempOut);
            float[] samples = new float[bytes.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = BitConverter.ToInt16(bytes, i * 2);
                samples[i] = sample / 32768f;
            }

            return samples;
        }
        finally
        {
            try
            {
                if (File.Exists(tempOut))
                    File.Delete(tempOut);
            }
            catch { }
        }
    }
}