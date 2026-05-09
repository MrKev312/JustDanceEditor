using Concentus;
using Concentus.Enums;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static partial class RakiAudioEncoder
{
    public static void EncodeToRakiNxOpus(WaveStream source, Stream output, IList<int>? markers = null, uint preSkip = 120, short outputGainDb256 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (source.WaveFormat.SampleRate != 48000)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);

        if (sampleProvider.WaveFormat.Channels != 2)
            sampleProvider = sampleProvider.ToMono().ToStereo();

        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO, TextWriter.Null);
        encoder.Bitrate = 192000;
        encoder.ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_20_MS;

        const int frameSize = 960;
        float[] bufferFloat = new float[frameSize * 2];
        short[] bufferShort = new short[frameSize * 2];
        byte[] opusPacketBuffer = new byte[1275];

        MemoryStream payloadStream = new();
        long totalOpusSamples = 0;

        using (BinaryWriter payloadWriter = new(payloadStream, Encoding.ASCII, leaveOpen: true))
        {
            int samplesRead;
            while ((samplesRead = sampleProvider.Read(bufferFloat, 0, bufferFloat.Length)) > 0)
            {
                if (samplesRead < bufferFloat.Length)
                    Array.Clear(bufferFloat, samplesRead, bufferFloat.Length - samplesRead);
                for (int i = 0; i < bufferFloat.Length; i++)
                {
                    float f = bufferFloat[i];
                    bufferShort[i] = (short)(Math.Clamp(f, -1.0f, 1.0f) * 32767);
                }

                int packetLen = encoder.Encode(bufferShort, frameSize, opusPacketBuffer, opusPacketBuffer.Length);
                if (packetLen > 0)
                {
                    WriteU32BE(payloadWriter, (uint)packetLen);
                    WriteU32BE(payloadWriter, encoder.FinalRange);
                    payloadWriter.Write(opusPacketBuffer, 0, packetLen);
                    totalOpusSamples += frameSize;
                }
            }
        }

        byte[] nxOpusData = payloadStream.ToArray();
        payloadStream.Dispose();

        MemoryStream headerStream = new();
        using (BinaryWriter nxWriter = new(headerStream, Encoding.UTF8, leaveOpen: true))
        {
            nxWriter.Write(0x80000001);
            nxWriter.Write((uint)0x18);
            nxWriter.Write((uint)sampleProvider.WaveFormat.Channels << 8);
            nxWriter.Write((uint)48000);
            nxWriter.Write((uint)0x20);
            nxWriter.Write(0u);
            nxWriter.Write(0u);
            nxWriter.Write(preSkip);
            nxWriter.Write(outputGainDb256);
            nxWriter.Write((byte)0x00);
            nxWriter.Write((byte)0x80);
            nxWriter.Write((uint)nxOpusData.Length);
            nxWriter.Write(nxOpusData);
        }

        byte[] fullNxData = headerStream.ToArray();
        headerStream.Dispose();

        bool hasMarkers = markers != null && markers.Count > 0;
        uint headerSize = hasMarkers ? 132u : 72u;
        uint dataStartOffset = (headerSize + 15) & ~15u;

        WriteRakiHeader(writer, "Nx  ", "Nx  ", headerSize, dataStartOffset, hasMarkers ? 5u : 3u, 3, false);

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(0x48u);
        writer.Write(0x10u);
        writer.Write(Encoding.ASCII.GetBytes("AdIn"));
        writer.Write(headerSize - 4);
        writer.Write(4u);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataStartOffset);
        writer.Write((uint)fullNxData.Length);

        writer.BaseStream.Position = 0x48;
        WriteU16(writer, 0x0063, false);
        WriteU16(writer, 2, false);
        WriteU32(writer, 48000, false);
        WriteU32(writer, 192000, false);
        WriteU16(writer, 4, false);
        WriteU16(writer, 16, false);

        writer.BaseStream.Position = headerSize - 4;
        writer.Write((uint)totalOpusSamples);

        writer.BaseStream.Position = dataStartOffset;
        writer.Write(fullNxData);
    }
}
