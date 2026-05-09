using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static partial class RakiAudioEncoder
{
    public static void EncodeToRakiPcm(WaveStream source, Stream output, string platform = "Win ", string type = "pcm ")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        WaveStream audioStream = source;
        if (source.WaveFormat.BitsPerSample != 16 || source.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            ISampleProvider sampleProvider = source.ToSampleProvider();
            audioStream = new SampleProvider16ToWaveStreamAdapter(sampleProvider, source.WaveFormat.SampleRate, source.WaveFormat.Channels);
        }

        bool isBigEndian = CheckIsBigEndian(platform);
        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        byte[] audioBuffer = new byte[audioStream.Length];
        audioStream.Position = 0;
        int bytesRead = audioStream.Read(audioBuffer, 0, audioBuffer.Length);

        WaveFormat format = audioStream.WaveFormat;
        ushort compressionCode = 1;

        if (isBigEndian && format.BitsPerSample == 16)
        {
            for (int i = 0; i < bytesRead; i += 2)
                (audioBuffer[i + 1], audioBuffer[i]) = (audioBuffer[i], audioBuffer[i + 1]);
        }

        uint rakiHeaderSize = 32;
        uint chunkTableSize = 24;
        uint currentOffset = rakiHeaderSize + chunkTableSize;

        uint fmtOffset = currentOffset;
        uint fmtSize = 18;
        currentOffset += fmtSize;

        uint alignment = 0x10;
        uint remainder = currentOffset % alignment;
        uint padding = remainder > 0 ? alignment - remainder : 0;

        uint dataOffset = currentOffset + padding;
        uint dataSize = (uint)bytesRead;

        WriteRakiHeader(writer, platform, type, fmtOffset + fmtSize, dataOffset, 2, 3, isBigEndian);

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, fmtOffset, isBigEndian);
        WriteU32(writer, fmtSize, isBigEndian);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        WriteU32(writer, dataOffset, isBigEndian);
        WriteU32(writer, dataSize, isBigEndian);

        WriteU16(writer, compressionCode, isBigEndian);
        WriteU16(writer, (ushort)format.Channels, isBigEndian);
        WriteU32(writer, (uint)format.SampleRate, isBigEndian);
        WriteU32(writer, (uint)format.AverageBytesPerSecond, isBigEndian);
        WriteU16(writer, (ushort)format.BlockAlign, isBigEndian);
        WriteU16(writer, (ushort)format.BitsPerSample, isBigEndian);
        WriteU16(writer, 0, isBigEndian);

        for (int i = 0; i < padding; i++)
            writer.Write((byte)0);

        writer.Write(audioBuffer, 0, bytesRead);
    }
}
