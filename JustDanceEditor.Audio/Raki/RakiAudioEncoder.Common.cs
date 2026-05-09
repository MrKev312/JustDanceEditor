using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static partial class RakiAudioEncoder
{
    private static void WriteRakiHeader(BinaryWriter writer, string platform, string type, uint headerSize, uint dataStartOffset, uint chunkCount, uint unk, bool isBigEndian, uint version = 0x0B)
    {
        writer.Write(Encoding.ASCII.GetBytes("RAKI"));
        WriteU32(writer, version, isBigEndian);
        writer.Write(Encoding.ASCII.GetBytes(platform.PadRight(4)[..4]));
        writer.Write(Encoding.ASCII.GetBytes(type.PadRight(4)[..4]));
        WriteU32(writer, headerSize, isBigEndian);
        WriteU32(writer, dataStartOffset, isBigEndian);
        WriteU32(writer, chunkCount, isBigEndian);
        WriteU32(writer, unk, isBigEndian);
    }

    private static void WriteU32(BinaryWriter writer, uint value, bool isBigEndian)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (isBigEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteU16(BinaryWriter writer, ushort value, bool isBigEndian)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (isBigEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteU32BE(BinaryWriter writer, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static bool CheckIsBigEndian(string platform) => platform.Trim() is "Wii" or "Cafe" or "PS3" or "X360";

    private sealed class SampleProvider16ToWaveStreamAdapter : WaveStream
    {
        private readonly WaveFormat _waveFormat;
        private readonly byte[] _audioBuffer;
        private long _position;

        public SampleProvider16ToWaveStreamAdapter(ISampleProvider sampleProvider, int sampleRate, int channels)
        {
            _waveFormat = new WaveFormat(sampleRate, 16, channels);
            const int bufferSize = 4096;
            float[] sampleBuffer = new float[bufferSize * channels];
            List<byte> pcmData = [];
            int read;
            while ((read = sampleProvider.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    short pcm16 = (short)(Math.Clamp(sampleBuffer[i], -1.0f, 1.0f) * 32767.0f);
                    pcmData.Add((byte)(pcm16 & 0xFF));
                    pcmData.Add((byte)((pcm16 >> 8) & 0xFF));
                }
            }

            _audioBuffer = [.. pcmData];
        }

        public override WaveFormat WaveFormat => _waveFormat;

        public override long Length => _audioBuffer.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = (int)Math.Min(count, _audioBuffer.Length - _position);
            if (toRead <= 0)
                return 0;

            Array.Copy(_audioBuffer, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }
    }
}
