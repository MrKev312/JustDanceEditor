using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public class AudioWaveformService
{
    public static async Task<float[]> GetWaveformDataAsync(string opusPath)
    {
        if (!File.Exists(opusPath))
            return [];

        // Use FFmpeg to dump raw PCM data to stdout
        // -f s16le: 16-bit little-endian
        // -ac 1: mono
        // -ar 8000: downsample to 8kHz for fast processing
        ProcessStartInfo startInfo = new()
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{opusPath}\" -f s16le -ac 1 -ar 8000 -",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(startInfo);
        if (process == null)
            return [];

        using MemoryStream ms = new();
        await process.StandardOutput.BaseStream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();

        float[] samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = BitConverter.ToInt16(bytes, i * 2);
            samples[i] = sample / 32768f;
        }

        return samples;
    }
}