using System;
using System.IO;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.Services;

public class AudioWaveformService
{
    public static async Task<float[]> GetWaveformDataAsync(string opusPath)
    {
        if (!File.Exists(opusPath))
            return [];

        string tempOut = Path.Combine(Path.GetTempPath(), $"jdi_wave_{Guid.NewGuid()}.raw");
        try
        {
            IConversion conversion = FFmpeg.Conversions.New();
            // Convert to raw 16-bit PCM, mono, 8kHz (matches previous behavior)
            conversion.AddParameter($"-y -i \"{opusPath}\" -f s16le -ac 1 -ar 8000");
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