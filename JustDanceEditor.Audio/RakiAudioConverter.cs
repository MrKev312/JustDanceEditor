using KevInc.Audio.Cafe.NAudio;
using KevInc.Audio.Nx.NAudio;
using KevInc.Audio.Xma2.NAudio;

using NAudio.Wave;

using System.Text;

namespace JustDanceEditor.Audio;

/// <summary>
/// Converts RAKI audio format to a WaveStream for in-memory processing.
/// Supports PCM, ADPCM (Windows), Nintendo Switch Opus, WiiU (Cafe) DSP ADPCM, and Xbox XMA2.
/// </summary>
public class RakiAudioConverter : IAudioConverter
{
    public Task<WaveStream> ConvertAsync(Stream source, string sourceFileName)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Task.Run(() => ConvertInternal(source));
    }

    private static WaveStream ConvertInternal(Stream source)
    {
        // If the source is not seekable, buffer it to a MemoryStream first
        Stream processingStream = source;
        if (!source.CanSeek)
        {
            MemoryStream memStream = new();
            source.CopyTo(memStream);
            memStream.Position = 0;
            processingStream = memStream;
        }

        return ProcessRaki(processingStream);
    }

    private static WaveStream ProcessRaki(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

        // --- HEADER PARSING ---
        long rakiOffset = 0;
        stream.Position = 0;

        uint magic = reader.ReadUInt32();
        if (magic == 0x494B4152) // "RAKI" LE
        {
            rakiOffset = 0;
        }
        else
        {
            stream.Position = 4;
            if (stream.Length >= 8 && reader.ReadUInt32() == 0x494B4152)
                rakiOffset = 4;
            else
                throw new InvalidDataException("Invalid header: 'RAKI' magic signature not found.");
        }

        stream.Position = rakiOffset + 0x08;
        string platform = Encoding.ASCII.GetString(reader.ReadBytes(4));
        string type = Encoding.ASCII.GetString(reader.ReadBytes(4));

        bool isBigEndian = CheckIsBigEndian(platform);

        stream.Position = rakiOffset + 0x10;
        uint headerSize = ReadU32(reader, isBigEndian);
        uint startOffset = ReadU32(reader, isBigEndian);
        uint chunkCount = ReadU32(reader, isBigEndian);

        stream.Position = rakiOffset + 0x20;
        uint firstChunkId = reader.ReadUInt32(); // Usually 'fmt '

        // Check for 'fmt ' in either endianness
        if (firstChunkId is not 0x20746D66 and not 0x666D7420)
            throw new InvalidDataException("Expected 'fmt ' chunk not found.");

        stream.Position = rakiOffset + 0x24;
        uint fmtChunkOffset = ReadU32(reader, isBigEndian);

        long dataSize = stream.Length - startOffset;
        if (TryGetChunkSize(reader, rakiOffset, headerSize, chunkCount, "data", isBigEndian, out uint chunkDataSize))
            dataSize = chunkDataSize;

        // --- STRATEGY SELECTION ---

        // Nintendo Switch Opus (Nx  / Nx  )
        if (platform == "Nx  " && type == "Nx  ")
        {
            return ConvertNxOpusToWaveStream(reader, startOffset, dataSize);
        }

        // WiiU / Cafe DSP ADPCM (Cafe / adpc) or (Wii / adpc)
        if ((platform == "Cafe" || platform == "Wii ") && type == "adpc")
        {
            return ConvertCafeAdpcmToWaveStream(reader, rakiOffset, startOffset, fmtChunkOffset, headerSize, isBigEndian);
        }

        // Xbox 360 XMA2
        if ((platform == "X360" || platform == "Dura") && type == "xma2")
        {
            return Xma2WaveDecoder.DecodeToWaveStream(reader, fmtChunkOffset, startOffset, dataSize, isBigEndian);
        }

        // Standard PCM / Windows ADPCM
        return ProcessStandardWav(stream, reader, platform, type, startOffset, dataSize, fmtChunkOffset, isBigEndian);
    }

    private static bool TryGetChunkSize(BinaryReader reader, long rakiOffset, uint headerSize, uint chunkCount, string magic, bool isBigEndian, out uint size)
    {
        long tableOffset = rakiOffset + 0x20;
        long tableEnd = chunkCount > 0
            ? tableOffset + (chunkCount * 12L)
            : rakiOffset + headerSize;

        tableEnd = Math.Min(tableEnd, reader.BaseStream.Length);
        for (long current = tableOffset; current + 12 <= tableEnd; current += 12)
        {
            reader.BaseStream.Position = current;
            string chunkMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            _ = ReadU32(reader, isBigEndian);
            uint chunkSize = ReadU32(reader, isBigEndian);

            if (chunkMagic == magic)
            {
                size = chunkSize;
                return true;
            }
        }

        size = 0;
        return false;
    }

    /// <summary>
    /// Converts Nintendo Switch Opus to a WaveStream using Concentus.
    /// </summary>
    private static WaveStream ConvertNxOpusToWaveStream(BinaryReader reader, uint startOffset, long dataSize)
    {
        return NintendoSwitchOpusWaveStreamFactory.DecodeToWaveStream(reader, startOffset, dataSize);
    }

    /// <summary>
    /// Converts WiiU (Cafe) DSP ADPCM to a PCM WaveStream.
    /// </summary>
    private static WaveStream ConvertCafeAdpcmToWaveStream(BinaryReader reader, long rakiOffset, uint startOffset, uint fmtChunkOffset, uint headerSize, bool isBigEndian)
    {
        // 1. Read Basic Format Info
        reader.BaseStream.Position = fmtChunkOffset;
        ushort compressionCode = ReadU16(reader, isBigEndian);
        ushort channels = ReadU16(reader, isBigEndian);
        uint sampleRate = ReadU32(reader, isBigEndian);
        // byteRate, blockAlign, etc. ignored for raw DSP decoding

        // 2. Determine Layout (Interleaved vs Planar/Split)
        bool isPlanar = false;
        long planarChunkSize = 0;

        if (channels > 1)
        {
            long currentChunkPtr = rakiOffset + 0x20 + 0x0C;
            while (currentChunkPtr < rakiOffset + headerSize)
            {
                reader.BaseStream.Position = currentChunkPtr;
                // Read Chunk ID
                byte[] idBytes = reader.ReadBytes(4);
                string idStr = Encoding.ASCII.GetString(idBytes);

                if (idStr == "datL")
                {
                    reader.BaseStream.Position = currentChunkPtr + 8;
                    planarChunkSize = ReadU32(reader, isBigEndian);
                    isPlanar = true;
                    break;
                }

                currentChunkPtr += 12; // Next chunk table entry
            }
        }

        // 3. Read DSP Coefficients
        reader.BaseStream.Position = rakiOffset + 0x30;
        uint dspInfoOffset = ReadU32(reader, isBigEndian);

        short[][] coefficients = new short[channels][];
        reader.BaseStream.Position = dspInfoOffset + 0x1C;

        for (int c = 0; c < channels; c++)
        {
            coefficients[c] = new short[16];
            for (int i = 0; i < 16; i++)
            {
                coefficients[c][i] = (short)ReadU16(reader, isBigEndian);
            }

            if (channels > 1 && c < channels - 1)
            {
                reader.BaseStream.Position = dspInfoOffset + 0x1C + ((c + 1) * 0x60);
            }
        }

        long dataStart = startOffset;
        long dataLength = reader.BaseStream.Length - startOffset;
        return CafeDspAdpcmWaveDecoder.DecodeToWaveStream(reader, dataStart, dataLength, channels, (int)sampleRate, isPlanar, planarChunkSize, coefficients);
    }

    /// <summary>
    /// Processes standard WAV/PCM/ADPCM RAKI formats.
    /// If MS ADPCM (Code 2), it software-decodes it to PCM.
    /// </summary>
    private static WaveStream ProcessStandardWav(Stream stream, BinaryReader reader, string platform, string type,
        uint startOffset, long dataSize, uint fmtChunkOffset, bool isBigEndian)
    {
        stream.Position = fmtChunkOffset;
        ushort compressionCode = ReadU16(reader, isBigEndian);
        ushort channels = ReadU16(reader, isBigEndian);
        uint sampleRate = ReadU32(reader, isBigEndian);
        uint byteRate = ReadU32(reader, isBigEndian);
        ushort blockAlign = ReadU16(reader, isBigEndian);
        ushort bitsPerSample = ReadU16(reader, isBigEndian);
        ushort extraSize = 0;
        byte[] extraData = [];

        if (compressionCode != 1)
        {
            if (stream.Position + 2 <= stream.Length)
            {
                extraSize = ReadU16(reader, isBigEndian);
                if (extraSize > 0)
                    extraData = reader.ReadBytes(extraSize);
            }
        }

        string fullTypeSignature = platform + type;
        bool isPcm = fullTypeSignature.Contains("pcm");
        bool isAdpcm = fullTypeSignature.Contains("adpc") && platform.StartsWith("Win");

        if (!isPcm && !isAdpcm)
        {
            throw new NotSupportedException($"The RAKI format '{platform}/{type}' is not supported by this converter.");
        }

        // If MS ADPCM (Code 2), decode to PCM in memory
        if (compressionCode == 2)
        {
            MemoryStream pcmStream = new();
            using (BinaryWriter writer = new(pcmStream, Encoding.UTF8, leaveOpen: true))
            {
                // Write standard PCM Header
                WriteWavHeader(writer, 0, 0, [], 1, channels, sampleRate, sampleRate * 2 * channels, (ushort)(channels * 2), 16);

                stream.Position = startOffset;
                byte[] adpcmData = reader.ReadBytes((int)dataSize);

                DecodeMsAdpcm(adpcmData, writer, channels, blockAlign, extraData);

                // Fix sizes
                long totalBytes = writer.BaseStream.Length - 8;
                writer.Seek(4, SeekOrigin.Begin);
                writer.Write((uint)totalBytes);
                writer.Seek(40, SeekOrigin.Begin);
                writer.Write((uint)(totalBytes - 36));
            }

            pcmStream.Position = 0;
            return new WaveFileReader(pcmStream);
        }

        // Build standard PCM WAV in memory
        MemoryStream wavStream = new();
        using (BinaryWriter writer = new(wavStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteWavHeader(writer, dataSize, extraSize, extraData,
                compressionCode, channels, sampleRate, byteRate, blockAlign, bitsPerSample);

            stream.Position = startOffset;
            CopyAndSwapData(reader, writer, dataSize, isBigEndian, compressionCode, bitsPerSample);
        }

        wavStream.Position = 0;
        return new WaveFileReader(wavStream);
    }

    // --- MS ADPCM DECODER ---

    private static readonly int[] MsAdpcmAdaptationTable =
    [
        230, 230, 230, 230, 307, 409, 512, 614,
        768, 614, 512, 409, 307, 230, 230, 230
    ];

    private static void DecodeMsAdpcm(byte[] data, BinaryWriter writer, int channels, int blockAlign, byte[] extraData)
    {
        // Parse Coefficients from extraData
        // Format: [SamplesPerBlock (2)] [NumCoeffs (2)] [Coeff1 (2) Coeff2 (2)]...
        if (extraData.Length < 4)
            throw new InvalidDataException("Missing coefficients for MS ADPCM.");

        // Offset 0 is SamplesPerBlock, ignored for now
        int numCoeffs = BitConverter.ToUInt16(extraData, 2);

        short[] coeff1 = new short[numCoeffs];
        short[] coeff2 = new short[numCoeffs];

        for (int i = 0; i < numCoeffs; i++)
        {
            // Coefficients start at offset 4
            int offset = 4 + (i * 4);
            if (offset + 4 > extraData.Length)
                break;

            coeff1[i] = BitConverter.ToInt16(extraData, offset);
            coeff2[i] = BitConverter.ToInt16(extraData, offset + 2);
        }

        int blockCount = data.Length / blockAlign;
        int dataOffset = 0;

        for (int b = 0; b < blockCount; b++)
        {
            int blockStart = dataOffset;

            // Read Block Header
            byte predictorL = data[dataOffset++];
            byte predictorR = (channels == 2) ? data[dataOffset++] : (byte)0;

            short deltaL = BitConverter.ToInt16(data, dataOffset);
            dataOffset += 2;
            short deltaR = (channels == 2) ? BitConverter.ToInt16(data, dataOffset) : (short)0;
            if (channels == 2)
                dataOffset += 2;

            short sample1L = BitConverter.ToInt16(data, dataOffset);
            dataOffset += 2;
            short sample1R = (channels == 2) ? BitConverter.ToInt16(data, dataOffset) : (short)0;
            if (channels == 2)
                dataOffset += 2;

            short sample2L = BitConverter.ToInt16(data, dataOffset);
            dataOffset += 2;
            short sample2R = (channels == 2) ? BitConverter.ToInt16(data, dataOffset) : (short)0;
            if (channels == 2)
                dataOffset += 2;

            // Write initial samples (Order: Sample2 then Sample1)
            writer.Write(sample2L);
            if (channels == 2)
                writer.Write(sample2R);
            writer.Write(sample1L);
            if (channels == 2)
                writer.Write(sample1R);

            // Decode Nibbles
            while (dataOffset < blockStart + blockAlign)
            {
                byte bVal = data[dataOffset++];

                if (channels == 2)
                {
                    // Left (High Nibble)
                    ProcessMsAdpcmNibble(bVal >> 4, predictorL, ref deltaL, ref sample1L, ref sample2L, coeff1, coeff2, writer);
                    // Right (Low Nibble)
                    ProcessMsAdpcmNibble(bVal & 0x0F, predictorR, ref deltaR, ref sample1R, ref sample2R, coeff1, coeff2, writer);
                }
                else
                {
                    // Mono Sample 1
                    ProcessMsAdpcmNibble(bVal >> 4, predictorL, ref deltaL, ref sample1L, ref sample2L, coeff1, coeff2, writer);
                    // Mono Sample 2
                    ProcessMsAdpcmNibble(bVal & 0x0F, predictorL, ref deltaL, ref sample1L, ref sample2L, coeff1, coeff2, writer);
                }
            }
        }
    }

    private static void ProcessMsAdpcmNibble(int nibble, int predictor, ref short delta, ref short samp1, ref short samp2, short[] c1, short[] c2, BinaryWriter writer)
    {
        if (predictor >= c1.Length)
            predictor = 0; // Safety check

        // Sign extend nibble (4-bit)
        int signedNibble = nibble >= 8 ? nibble - 16 : nibble;

        int predictorCoeff1 = c1[predictor];
        int predictorCoeff2 = c2[predictor];

        int prediction = ((samp1 * predictorCoeff1) + (samp2 * predictorCoeff2)) / 256;
        int newSample = prediction + (signedNibble * delta);

        // Clamp
        if (newSample > 32767)
            newSample = 32767;
        if (newSample < -32768)
            newSample = -32768;

        // Update state
        samp2 = samp1;
        samp1 = (short)newSample;

        // Adapt delta
        delta = (short)(MsAdpcmAdaptationTable[nibble] * delta / 256);
        if (delta < 16)
            delta = 16;

        writer.Write(samp1);
    }

    private static void CopyAndSwapData(BinaryReader reader, BinaryWriter writer, long dataSize, bool isBigEndian, ushort compressionCode, ushort bitsPerSample)
    {
        byte[] buffer = new byte[64 * 1024];
        int bytesRead;
        long totalRead = 0;
        bool swapNeeded = isBigEndian && compressionCode == 1 && bitsPerSample == 16;

        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0 && totalRead < dataSize)
        {
            int toWrite = (int)Math.Min(bytesRead, dataSize - totalRead);
            if (swapNeeded)
            {
                for (int i = 0; i < toWrite; i += 2)
                {
                    (buffer[i + 1], buffer[i]) = (buffer[i], buffer[i + 1]);
                }
            }

            writer.Write(buffer, 0, toWrite);
            totalRead += toWrite;
        }
    }

    private static void WriteWavHeader(BinaryWriter writer, long dataSize, ushort extraSize, byte[] extraData,
        ushort compressionCode, ushort channels, uint sampleRate, uint byteRate, ushort blockAlign, ushort bitsPerSample)
    {
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        uint fmtChunkLen = (uint)(16 + (extraSize > 0 ? 2 + extraSize : 0));
        uint riffSize = 4 + (4 + 4 + fmtChunkLen) + 4 + 4 + (uint)dataSize;
        writer.Write(riffSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(fmtChunkLen);
        writer.Write(compressionCode);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        if (extraSize > 0)
        {
            writer.Write(extraSize);
            writer.Write(extraData);
        }

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((uint)dataSize);
    }

    private static bool CheckIsBigEndian(string platform) => platform switch
    {
        "Wii " => true,
        "Cafe" => true,
        "PS3 " => true,
        "X360" => true,
        _ => false
    };

    private static uint ReadU32(BinaryReader reader, bool isBigEndian)
    {
        byte[] b = reader.ReadBytes(4);
        if (isBigEndian)
            Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static ushort ReadU16(BinaryReader reader, bool isBigEndian)
    {
        byte[] b = reader.ReadBytes(2);
        if (isBigEndian)
            Array.Reverse(b);
        return BitConverter.ToUInt16(b, 0);
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

}
