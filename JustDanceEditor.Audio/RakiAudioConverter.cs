using Concentus.Enums;
using Concentus.Structs;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

/// <summary>
/// Converts RAKI audio format to a WaveStream for in-memory processing.
/// Supports PCM, ADPCM (Windows), Nintendo Switch Opus, and WiiU (Cafe) DSP ADPCM.
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

        stream.Position = rakiOffset + 0x20;
        uint firstChunkId = reader.ReadUInt32(); // Usually 'fmt '

        // Check for 'fmt ' in either endianness
        if (firstChunkId is not 0x20746D66 and not 0x666D7420)
            throw new InvalidDataException("Expected 'fmt ' chunk not found.");

        stream.Position = rakiOffset + 0x24;
        uint fmtChunkOffset = ReadU32(reader, isBigEndian);

        long dataSize = stream.Length - startOffset;

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

        // Standard PCM / Windows ADPCM
        return ProcessStandardWav(stream, reader, platform, type, startOffset, dataSize, fmtChunkOffset, isBigEndian);
    }

    /// <summary>
    /// Converts Nintendo Switch Opus to a WaveStream using Concentus.
    /// </summary>
    private static WaveStream ConvertNxOpusToWaveStream(BinaryReader reader, uint startOffset, long dataSize)
    {
        MemoryStream oggStream = new();
        NxOpusToOggConverter.ConvertToOgg(reader, startOffset, dataSize, oggStream);
        oggStream.Position = 0;
        return new OpusWaveStream(oggStream, ownsStream: true);
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

        // 4. Decode to PCM
        MemoryStream pcmStream = new();
        using (BinaryWriter writer = new(pcmStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteWavHeader(writer, 0, 0, [], 1, channels, sampleRate, sampleRate * 2 * channels, (ushort)(2 * channels), 16);

            long dataStart = startOffset;
            long dataLength = reader.BaseStream.Length - startOffset;

            DecodeGcAdpcm(reader, writer, dataStart, dataLength, channels, isPlanar, planarChunkSize, coefficients);

            long totalBytes = writer.BaseStream.Length - 8;
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write((uint)totalBytes);
            writer.Seek(40, SeekOrigin.Begin);
            writer.Write((uint)(totalBytes - 36)); // Data size
        }

        pcmStream.Position = 0;
        return new WaveFileReader(pcmStream);
    }

    private static void DecodeGcAdpcm(BinaryReader reader, BinaryWriter writer, long dataStart, long dataLength, int channels, bool isPlanar, long planarChunkSize, short[][] coeffs)
    {
        short[] hist1 = new short[channels];
        short[] hist2 = new short[channels];

        long totalFrames = (isPlanar ? planarChunkSize : (dataLength / channels)) / 8;

        short[][] pcmBuffer = new short[channels][];
        for (int i = 0; i < channels; i++)
            pcmBuffer[i] = new short[14];

        for (long f = 0; f < totalFrames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                long readPos;
                if (isPlanar)
                {
                    long channelOffset = c == 0 ? 0 : (planarChunkSize + 8);
                    readPos = dataStart + channelOffset + (f * 8);
                }
                else
                {
                    readPos = dataStart + (f * 8 * channels) + (c * 8);
                }

                if (readPos + 8 > reader.BaseStream.Length)
                    break;

                reader.BaseStream.Position = readPos;
                byte[] frame = reader.ReadBytes(8);

                DecodeDspFrame(frame, coeffs[c], ref hist1[c], ref hist2[c], pcmBuffer[c]);
            }

            for (int s = 0; s < 14; s++)
            {
                for (int c = 0; c < channels; c++)
                {
                    writer.Write(pcmBuffer[c][s]);
                }
            }
        }
    }

    private static void DecodeDspFrame(byte[] frame, short[] coeffs, ref short hist1, ref short hist2, short[] outSamples)
    {
        byte header = frame[0];
        int scale = 1 << (header & 0x0F);
        int predictorIdx = (header >> 4) & 0x0F;

        if (predictorIdx > 7)
            predictorIdx = 0;

        short coef1 = coeffs[predictorIdx * 2];
        short coef2 = coeffs[(predictorIdx * 2) + 1];

        short yn1 = hist1;
        short yn2 = hist2;

        for (int i = 0; i < 14; i++)
        {
            int byteIndex = 1 + (i / 2);
            int nibble = (i % 2 == 0) ? (frame[byteIndex] >> 4) & 0x0F : frame[byteIndex] & 0x0F;
            if (nibble >= 8)
                nibble -= 16;

            long prediction = ((long)coef1 * yn1) + ((long)coef2 * yn2);
            long val = ((long)nibble * scale) << 11;
            val += prediction;
            long final = (val + 1024) >> 11;

            if (final > 32767)
                final = 32767;
            if (final < -32768)
                final = -32768;

            short outSample = (short)final;
            outSamples[i] = outSample;

            yn2 = yn1;
            yn1 = outSample;
        }

        hist1 = yn1;
        hist2 = yn2;
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

    private static void WriteU32BE(BinaryWriter writer, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteU16BE(BinaryWriter writer, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    // --- Encoder stubs/helpers for Export ---

    internal static class NxOpusToOggConverter
    {
        private static readonly uint[] CrcTable = MakeCrcTable();

        public static void ConvertToOgg(BinaryReader reader, uint startOffset, long dataSize, Stream outputStream)
        {
            // Ensure we leave the output stream open, as we need to read from it later
            using BinaryWriter writer = new(outputStream, Encoding.UTF8, leaveOpen: true);

            reader.BaseStream.Position = startOffset;

            // 1. Read Nintendo Opus Header (Little Endian)
            reader.BaseStream.Seek(0x10, SeekOrigin.Current);
            uint headerInnerSize = reader.ReadUInt32();

            reader.BaseStream.Seek(0x08, SeekOrigin.Current);
            uint preSkip = reader.ReadUInt32();

            short outputGainDb256 = reader.ReadInt16();

            long payloadStart = startOffset + headerInnerSize + 0x08;
            long payloadLength = dataSize - (headerInnerSize + 0x08);

            reader.BaseStream.Position = payloadStart;

            // 2. Write Ogg ID Header
            WriteOggHeader(writer, 2, preSkip, 48000, outputGainDb256);

            // 3. Write Ogg Comment Header
            WriteOggComment(writer, 2);

            // 4. Process Packets
            long currentPos = 0;
            long granulePos = 0;
            uint sequence = 2;

            while (currentPos < payloadLength)
            {
                // Nx Packet: [Size (4B BE)] [Unk (4B)] [Opus Data]
                byte[] sizeBytes = reader.ReadBytes(4);
                if (sizeBytes.Length < 4)
                    break;

                Array.Reverse(sizeBytes);
                uint packetSize = BitConverter.ToUInt32(sizeBytes, 0);

                reader.ReadBytes(4); // Skip unknown

                if (packetSize == 0)
                    break;

                byte[] opusData = reader.ReadBytes((int)packetSize);

                currentPos += 4 + 4 + packetSize;

                int samples = GetOpusSamples(opusData);
                granulePos += samples;

                WriteOggPage(writer, opusData, granulePos, sequence++);
            }
        }

        private static void WriteOggHeader(BinaryWriter writer, byte channels, uint preSkip, uint outputSampleRate, short outputGainDb256 = 0)
        {
            byte[] headData = new byte[19];
            Encoding.ASCII.GetBytes("OpusHead").CopyTo(headData, 0);
            headData[8] = 1; // Version
            headData[9] = channels;
            BitConverter.GetBytes((ushort)preSkip).CopyTo(headData, 10);
            BitConverter.GetBytes(outputSampleRate).CopyTo(headData, 12);
            BitConverter.GetBytes(outputGainDb256).CopyTo(headData, 16);
            headData[18] = 0; // Mapping Family

            WriteOggPage(writer, headData, 0, 0, true, false);
        }

        private static void WriteOggComment(BinaryWriter writer, uint sequence)
        {
            string vendor = "vgmstream-port";
            byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);

            int len = 8 + 4 + vendorBytes.Length + 4;
            byte[] commentData = new byte[len];

            Encoding.ASCII.GetBytes("OpusTags").CopyTo(commentData, 0);
            BitConverter.GetBytes(vendorBytes.Length).CopyTo(commentData, 8);
            vendorBytes.CopyTo(commentData, 12);
            BitConverter.GetBytes(0).CopyTo(commentData, 12 + vendorBytes.Length);

            WriteOggPage(writer, commentData, 0, sequence);
        }

        private static void WriteOggPage(BinaryWriter writer, byte[] packetData, long granulePos, uint sequence, bool isBos = false, bool isEos = false)
        {
            byte headerType = 0;
            if (isBos)
                headerType |= 2;
            if (isEos)
                headerType |= 4;

            int segments = (packetData.Length + 255) / 255;
            int pageSize = 27 + segments + packetData.Length;
            byte[] page = new byte[pageSize];

            // 'OggS'
            page[0] = 0x4F;
            page[1] = 0x67;
            page[2] = 0x67;
            page[3] = 0x53;
            page[4] = 0;
            page[5] = headerType;

            BitConverter.GetBytes(granulePos).CopyTo(page, 6);
            BitConverter.GetBytes(0x12345678).CopyTo(page, 14); // Arbitrary Serial
            BitConverter.GetBytes(sequence).CopyTo(page, 18);
            page[26] = (byte)segments;

            // Lacing
            int remaining = packetData.Length;
            for (int i = 0; i < segments; i++)
            {
                int val = Math.Min(remaining, 255);
                page[27 + i] = (byte)val;
                remaining -= val;
            }

            // Payload
            Array.Copy(packetData, 0, page, 27 + segments, packetData.Length);

            // Checksum
            uint crc = 0;
            for (int i = 0; i < pageSize; i++)
            {
                crc = (crc << 8) ^ CrcTable[((crc >> 24) & 0xff) ^ page[i]];
            }

            BitConverter.GetBytes(crc).CopyTo(page, 22);

            writer.Write(page);
        }

        private static int GetOpusSamples(byte[] data)
        {
            if (data.Length == 0)
                return 0;
            int toc = data[0];
            int config = (toc >> 3) & 0x1F;
            int count = toc & 0x3;

            int frameSize;
            if ((config & 0x10) == 0) // Silk-only
            {
                if ((config & 0x08) == 0)
                    frameSize = 480;
                else if ((config & 0x04) == 0)
                    frameSize = 960;
                else if ((config & 0x02) == 0)
                    frameSize = 1920;
                else
                    frameSize = 2880;
            }
            else // Hybrid or CELT
            {
                int c = (config >> 1) & 0x07;
                frameSize = c switch { 0 => 120, 1 => 240, 2 => 480, 3 => 960, _ => 960 };
            }

            int frames = 1;
            if (count == 0)
                frames = 1;
            else if (count is 1 or 2)
                frames = 2;
            else if (count == 3 && data.Length > 1)
                frames = data[1] & 0x3F;

            return frameSize * frames;
        }

        private static uint[] MakeCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint r = i << 24;
                for (int j = 0; j < 8; j++)
                    r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04c11db7 : (r << 1);
                table[i] = r;
            }

            return table;
        }
    }

    public static void EncodeToRakiPcm(WaveStream source, Stream output, string platform = "Win ", string type = "pcm ")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        // Convert to 16-bit PCM if necessary
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

        if (isBigEndian && compressionCode == 1 && format.BitsPerSample == 16)
        {
            for (int i = 0; i < bytesRead; i += 2)
            {
                (audioBuffer[i + 1], audioBuffer[i]) = (audioBuffer[i], audioBuffer[i + 1]);
            }
        }

        uint rakiHeaderSize = 32;
        uint chunkTableSize = 24;
        uint currentOffset = rakiHeaderSize + chunkTableSize;
        uint fmtOffset = currentOffset;
        uint fmtSize = 16;
        currentOffset += fmtSize;
        uint dataOffset = currentOffset;
        uint dataSize = (uint)bytesRead;
        uint totalHeaderSize = dataOffset;

        WriteRakiHeader(writer, platform, type, totalHeaderSize, dataOffset, 2, 0, isBigEndian);

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

        writer.Write(audioBuffer, 0, bytesRead);
    }

    public static void EncodeToRakiNxOpus(WaveStream source, Stream output, IList<int>? markers = null, uint preSkip = 120, short outputGainDb256 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (source.WaveFormat.SampleRate != 48000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);
        }

        if (sampleProvider.WaveFormat.Channels != 2)
        {
            if (sampleProvider.WaveFormat.Channels == 1)
                sampleProvider = sampleProvider.ToStereo();
            else
                sampleProvider = sampleProvider.ToMono().ToStereo();
        }

        OpusEncoder encoder = new(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO)
        {
            Bitrate = 192000,
            ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_20_MS
        };

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
                {
                    Array.Clear(bufferFloat, samplesRead, bufferFloat.Length - samplesRead);
                }

                for (int i = 0; i < bufferFloat.Length; i++)
                {
                    float f = bufferFloat[i];
                    if (f > 1.0f)
                        f = 1.0f;
                    else if (f < -1.0f)
                        f = -1.0f;
                    bufferShort[i] = (short)(f * 32767);
                }

                int packetLen;
                try
                {
                    packetLen = encoder.Encode(bufferShort, 0, frameSize, opusPacketBuffer, 0, opusPacketBuffer.Length);
                }
                catch (Concentus.OpusException)
                {
                    continue;
                }

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

        uint headerInnerSize = 0x20;
        MemoryStream headerStream = new();
        using (BinaryWriter nxWriter = new(headerStream, Encoding.UTF8, leaveOpen: true))
        {
            nxWriter.Write(0x80000001);
            nxWriter.Write((uint)0x18);
            uint channels = (uint)sampleProvider.WaveFormat.Channels;
            nxWriter.Write(channels << 8);
            nxWriter.Write((uint)48000);
            nxWriter.Write(headerInnerSize);
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
        List<(string magic, uint offset, uint size)> chunks = [];

        uint chunkTableStart = 0x20;
        uint chunkTableSize = hasMarkers ? 60u : 36u;
        uint currentDataOffset = chunkTableStart + chunkTableSize;

        uint fmtDataOffset = currentDataOffset;
        uint fmtDataSize = 0x10;
        chunks.Add(("fmt ", fmtDataOffset, fmtDataSize));
        currentDataOffset += fmtDataSize;

        byte[]? markData = null;
        if (hasMarkers)
        {
            markData = GenerateMarkChunk(markers!);
            uint markSize = (uint)markData.Length;
            chunks.Add(("MARK", currentDataOffset, markSize));
            currentDataOffset += markSize;
        }

        byte[]? strgData = null;
        if (hasMarkers)
        {
            strgData = GenerateStrgChunk(markers!);
            uint strgSize = (uint)strgData.Length;
            chunks.Add(("STRG", currentDataOffset, strgSize));
            currentDataOffset += strgSize;
        }

        uint adInDataOffset = currentDataOffset;
        uint adInDataSize = 0x04;
        chunks.Add(("AdIn", adInDataOffset, adInDataSize));
        currentDataOffset += adInDataSize;

        uint headerSize = currentDataOffset;
        uint alignment = 0x10;
        uint remainder = headerSize % alignment;
        uint padding = remainder > 0 ? alignment - remainder : 0;
        uint dataStartOffset = headerSize + padding;

        uint dataSize = (uint)fullNxData.Length;
        chunks.Add(("data", dataStartOffset, dataSize));

        WriteRakiHeader(writer, "Nx  ", "Nx  ", headerSize, dataStartOffset, (uint)chunks.Count, 3, false);

        foreach ((string magic, uint offset, uint size) in chunks)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(offset);
            writer.Write(size);
        }

        WriteU16(writer, 0x0063, false);
        WriteU16(writer, 2, false);
        WriteU32(writer, 48000, false);
        WriteU32(writer, 192000, false);
        WriteU16(writer, 4, false);
        WriteU16(writer, 16, false);

        if (hasMarkers && markData != null)
            writer.Write(markData);

        if (hasMarkers && strgData != null)
            writer.Write(strgData);

        writer.Write((uint)totalOpusSamples);

        while (writer.BaseStream.Position < dataStartOffset)
        {
            writer.Write((byte)0);
        }

        writer.Write(fullNxData);
    }

    private static void WriteRakiHeader(BinaryWriter writer, string platform, string type, uint headerSize, uint dataStartOffset, uint chunkCount, uint unk, bool isBigEndian)
    {
        writer.Write(Encoding.ASCII.GetBytes("RAKI"));
        WriteU32(writer, 0x0B, isBigEndian);
        writer.Write(Encoding.ASCII.GetBytes(platform));
        writer.Write(Encoding.ASCII.GetBytes(type));
        WriteU32(writer, headerSize, isBigEndian);
        WriteU32(writer, dataStartOffset, isBigEndian);
        WriteU32(writer, chunkCount, isBigEndian);
        WriteU32(writer, unk, isBigEndian);
    }

    private static byte[] GenerateMarkChunk(IList<int> markers)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        WriteU32BE(w, (uint)markers.Count);
        foreach (int m in markers)
            WriteU32BE(w, (uint)m);
        for (int i = 0; i < markers.Count; i++)
            WriteU16BE(w, (ushort)i);
        return ms.ToArray();
    }

    private static byte[] GenerateStrgChunk(IList<int> markers)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        WriteU32BE(w, (uint)markers.Count);
        int currentOffset = 4 + (markers.Count * 2);
        List<string> labels = [];
        for (int i = 0; i < markers.Count; i++)
            labels.Add($"{(i / 4) + 1}.{(i % 4) + 1}");
        foreach (string label in labels)
        {
            WriteU16BE(w, (ushort)currentOffset);
            currentOffset += label.Length + 1;
        }

        foreach (string label in labels)
        {
            w.Write(Encoding.ASCII.GetBytes(label));
            w.Write((byte)0);
        }

        return ms.ToArray();
    }

    private sealed class SampleProvider16ToWaveStreamAdapter : WaveStream
    {
        private readonly WaveFormat _waveFormat;
        private byte[] _audioBuffer;
        private long _position = 0;

        public SampleProvider16ToWaveStreamAdapter(ISampleProvider sampleProvider, int sampleRate, int channels)
        {
            _waveFormat = new WaveFormat(sampleRate, 16, channels);
            _audioBuffer = ConvertSamplesToPcm16(sampleProvider);
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
            int bytesToRead = Math.Min(count, (int)(_audioBuffer.Length - _position));
            if (bytesToRead <= 0)
                return 0;

            Array.Copy(_audioBuffer, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        private byte[] ConvertSamplesToPcm16(ISampleProvider sampleProvider)
        {
            const int bufferSize = 4096;
            float[] sampleBuffer = new float[bufferSize * _waveFormat.Channels];
            List<byte> pcmData = [];

            int samplesRead;
            while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    float sample = sampleBuffer[i];
                    short pcm16 = (short)Math.Max(-32768, Math.Min(32767, sample * 32767.0f));

                    pcmData.Add((byte)(pcm16 & 0xFF));
                    pcmData.Add((byte)((pcm16 >> 8) & 0xFF));
                }
            }

            return [.. pcmData];
        }
    }
}