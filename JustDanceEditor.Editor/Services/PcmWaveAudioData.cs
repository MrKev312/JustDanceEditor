using System;
using System.IO;
using System.Text;

namespace JustDanceEditor.Editor.Services;

internal sealed record PcmWaveAudioData(int SampleRate, int Channels, short[] Samples)
{
    public long FrameCount => Samples.Length / Math.Max(1, Channels);
    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

    public static PcmWaveAudioData Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream, Encoding.ASCII);

        if (ReadFourCc(reader) != "RIFF")
            throw new InvalidDataException("The audio preview file is not a RIFF WAV file.");

        reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
            throw new InvalidDataException("The audio preview file is not a WAV file.");

        ushort formatTag = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCc(reader);
            uint chunkSize = reader.ReadUInt32();
            long nextChunk = stream.Position + chunkSize + (chunkSize & 1);

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                if (chunkSize > int.MaxValue)
                    throw new InvalidDataException("The audio preview file is too large.");

                data = reader.ReadBytes((int)chunkSize);
            }

            stream.Position = Math.Min(nextChunk, stream.Length);
        }

        if (data == null)
            throw new InvalidDataException("The audio preview file does not contain PCM data.");

        bool isPcm = formatTag is 1 or 65534;
        if (!isPcm || bitsPerSample != 16 || channels == 0 || sampleRate == 0)
            throw new InvalidDataException("Only 16-bit PCM WAV preview files are supported.");

        short[] samples = new short[data.Length / sizeof(short)];
        Buffer.BlockCopy(data, 0, samples, 0, samples.Length * sizeof(short));

        return new PcmWaveAudioData((int)sampleRate, channels, samples);
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException();

        return Encoding.ASCII.GetString(bytes);
    }
}
