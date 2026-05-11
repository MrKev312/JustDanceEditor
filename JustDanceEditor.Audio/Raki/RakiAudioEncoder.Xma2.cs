using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static partial class RakiAudioEncoder
{
    public static void EncodeToRakiXma2(WaveStream source, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (sampleProvider.WaveFormat.SampleRate != 48000)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);

        if (sampleProvider.WaveFormat.Channels != 2)
            sampleProvider = sampleProvider.ToMono().ToStereo();

        using WaveStream pcmStream = new SampleProvider16ToWaveStreamAdapter(sampleProvider, 48000, 2);
        byte[] pcmBytes = new byte[pcmStream.Length];
        pcmStream.Position = 0;
        pcmStream.ReadExactly(pcmBytes);

        short[] pcm = new short[pcmBytes.Length / sizeof(short)];
        Buffer.BlockCopy(pcmBytes, 0, pcm, 0, pcmBytes.Length);

        byte[] xmaData = KevInc.Audio.Xma2.Xma2Encoder.Encode(pcm, 48000, 2, out int samplesEncoded);

        const bool isBigEndian = true;
        const uint rakiHeaderSize = 32;
        const uint chunkCount = 2;
        const uint chunkTableSize = chunkCount * 12;
        const uint fmtOffset = rakiHeaderSize + chunkTableSize;
        const uint fmtSize = 0x34;
        const uint headerSize = fmtOffset + fmtSize;
        const uint alignment = 0x800;
        uint dataOffset = (headerSize + alignment - 1) & ~(alignment - 1);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);
        WriteRakiHeader(writer, "X360", "xma2", headerSize, dataOffset, chunkCount, 3, isBigEndian, version: 9);

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, fmtOffset, isBigEndian);
        WriteU32(writer, fmtSize, isBigEndian);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        WriteU32(writer, dataOffset, isBigEndian);
        WriteU32(writer, (uint)xmaData.Length, isBigEndian);

        writer.BaseStream.Position = fmtOffset;
        WriteU16(writer, 0x0166, isBigEndian);
        WriteU16(writer, 2, isBigEndian);
        WriteU32(writer, 48000, isBigEndian);
        WriteU32(writer, 48000 * 2, isBigEndian);
        WriteU16(writer, 4, isBigEndian);
        WriteU16(writer, 16, isBigEndian);
        WriteU16(writer, 0x22, isBigEndian);
        WriteU16(writer, 1, isBigEndian);
        WriteU32(writer, 0x03, isBigEndian);
        WriteU32(writer, (uint)samplesEncoded, isBigEndian);
        WriteU32(writer, 0x8000, isBigEndian);
        WriteU32(writer, 0, isBigEndian);
        WriteU32(writer, (uint)samplesEncoded, isBigEndian);
        WriteU32(writer, 0, isBigEndian);
        WriteU32(writer, 0, isBigEndian);
        writer.Write((byte)0);
        writer.Write((byte)4);
        WriteU16(writer, (ushort)Math.Min(ushort.MaxValue, xmaData.Length / 0x8000), isBigEndian);

        writer.BaseStream.Position = dataOffset;
        writer.Write(xmaData);
    }
}
