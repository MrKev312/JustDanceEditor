using NAudio.Wave;

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
        uint firstChunkId = reader.ReadUInt32(); // Usually 'fmt ' (0x666D7420) or BE equivalent

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
        ushort compressionCode = ReadU16(reader, isBigEndian); // Should be 0 (DSP) or specific value, ignored by decoder logic usually
        ushort channels = ReadU16(reader, isBigEndian);
        uint sampleRate = ReadU32(reader, isBigEndian);
        // byteRate, blockAlign, etc. ignored for raw DSP decoding

        // 2. Determine Layout (Interleaved vs Planar/Split)
        // RAKI files for Cafe can have "datL" chunks implying split stereo.
        bool isPlanar = false;
        long planarChunkSize = 0;

        if (channels > 1)
        {
            // Scan chunks after "fmt "
            long currentChunkPtr = rakiOffset + 0x20 + 0x0C; // Skip the entry for 'fmt '
            while (currentChunkPtr < rakiOffset + headerSize)
            {
                reader.BaseStream.Position = currentChunkPtr;
                uint chunkId = reader.ReadUInt32(); // Read as native LE to check signature

                // "datL" in ASCII is 0x6461744C. In BE file, ReadUInt32 reads it reversed on LE machine? 
                // Let's just read bytes to be safe against endianness confusion in detection.
                reader.BaseStream.Position = currentChunkPtr;
                byte[] idBytes = reader.ReadBytes(4);
                string idStr = Encoding.ASCII.GetString(idBytes);

                if (idStr == "datL")
                {
                    reader.BaseStream.Position = currentChunkPtr + 8; // Offset (4), Size (4)
                    planarChunkSize = ReadU32(reader, isBigEndian);
                    isPlanar = true;
                    break;
                }

                currentChunkPtr += 12; // Next chunk table entry
            }
        }

        // 3. Read DSP Coefficients
        // RAKI header offset 0x30 points to DSP Info. Info + 0x1C is usually the Coeffs.
        reader.BaseStream.Position = rakiOffset + 0x30;
        uint dspInfoOffset = ReadU32(reader, isBigEndian);

        // Read 16 coefficients (shorts) per channel
        short[][] coefficients = new short[channels][];
        reader.BaseStream.Position = dspInfoOffset + 0x1C;

        for (int c = 0; c < channels; c++)
        {
            coefficients[c] = new short[16];
            for (int i = 0; i < 16; i++)
            {
                coefficients[c][i] = (short)ReadU16(reader, isBigEndian);
            }

            // In standard multi-channel DSP headers, usually all coeffs are contiguous.
            // However, some formats pad to 0x60 bytes. 
            // The RAKI "Cafe" reference implies coeffs are contiguous here for the initial set.
            // If channels > 1 and it's not contiguous, we might need logic to skip padding,
            // but standard Ubi RAKI usually packs them or uses standard DSP header spacing (0x60).
            // Let's try contiguous first, but if it sounds broken, we might need to skip (0x60 - 32) bytes.
            // Based on vgmstream: `dsp_read_coefs(..., 0x60, ...)` implies stride is 0x60? 
            // No, 0x60 is the length read.
            // Actually, for multi-channel RAKI, the DSP info block might only contain one set if they share?
            // No, usually stereo has distinct coeffs. 
            // We will assume packed 16 shorts per channel for now unless stride logic dictates otherwise.
            // Re-checking reference: `dsp_read_coefs` reads generic DSP header. 
            // If the loop context is stored, it's 0x60 bytes PER channel.
            if (channels > 1 && c < channels - 1)
            {
                // Align to next 0x60 bytes? 
                // In Ubi RAKI, the dsp_coef pointer is global. 
                // Let's assume standard DSP header spacing of 0x60 bytes per channel entry.
                // 32 bytes coeffs + 14 bytes misc + padding...
                reader.BaseStream.Position = dspInfoOffset + 0x1C + ((c + 1) * 0x60);
            }
        }

        // 4. Decode to PCM
        MemoryStream pcmStream = new();
        using (BinaryWriter writer = new(pcmStream, Encoding.UTF8, leaveOpen: true))
        {
            // Write temporary WAV header
            WriteWavHeader(writer, 0, 0, [], 1, channels, sampleRate, sampleRate * 2 * channels, (ushort)(2 * channels), 16);

            long dataStart = startOffset;
            long dataLength = reader.BaseStream.Length - startOffset;

            DecodeGcAdpcm(reader, writer, dataStart, dataLength, channels, isPlanar, planarChunkSize, coefficients);

            // Fix WAV header data size
            long totalBytes = writer.BaseStream.Length - 44;
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write((uint)(totalBytes + 36));
            writer.Seek(40, SeekOrigin.Begin);
            writer.Write((uint)totalBytes);
        }

        pcmStream.Position = 0;
        return new WaveFileReader(pcmStream);
    }

    private static void DecodeGcAdpcm(BinaryReader reader, BinaryWriter writer, long dataStart, long dataLength, int channels, bool isPlanar, long planarChunkSize, short[][] coeffs)
    {
        // DSP ADPCM: 8 bytes = 14 samples.
        // Interleaved: 8 bytes Ch1, 8 bytes Ch2...
        // Planar: [HeaderL][DataL]... [HeaderR][DataR]... (Usually 'datL' includes an 8-byte header itself inside the chunk data if counted purely)
        // But RAKI startOffset usually points to the chunk payload area. 

        // Prepare decoding state
        short[] hist1 = new short[channels];
        short[] hist2 = new short[channels];

        // Total samples to decode (approx)
        // Each 8 bytes = 14 samples.
        long totalFrames = (isPlanar ? planarChunkSize : (dataLength / channels)) / 8;

        // Buffers for one frame of samples
        short[][] pcmBuffer = new short[channels][];
        for (int i = 0; i < channels; i++)
            pcmBuffer[i] = new short[14];

        for (long f = 0; f < totalFrames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                // Position reader
                long readPos;
                if (isPlanar)
                {
                    // Data Start + (Channel * (PlanarSize + Padding?)) + (Frame * 8)
                    // The "datL" logic implies Planar Chunk Size includes the header? 
                    // Let's assume compact planar data blocks.
                    // Note: RAKI Planar chunks have 8 byte headers (ID+Size) which we skipped via startOffset?
                    // Actually, startOffset usually points to the FIRST chunk data.
                    // So Ch0 starts at startOffset. 
                    // Ch1 starts at startOffset + PlanarChunkSize + 8 (Skip 'datR' header).

                    long channelOffset = c == 0 ? 0 : (planarChunkSize + 8);
                    readPos = dataStart + channelOffset + (f * 8);
                }
                else
                {
                    // Standard Interleave: [8b Ch0] [8b Ch1] [8b Ch0]...
                    readPos = dataStart + (f * 8 * channels) + (c * 8);
                }

                if (readPos + 8 > reader.BaseStream.Length)
                    break;

                reader.BaseStream.Position = readPos;
                byte[] frame = reader.ReadBytes(8);

                DecodeDspFrame(frame, coeffs[c], ref hist1[c], ref hist2[c], pcmBuffer[c]);
            }

            // Write interleaved PCM samples
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

        // Clamp predictor index just in case
        if (predictorIdx > 7)
            predictorIdx = 0;

        short coef1 = coeffs[predictorIdx * 2];
        short coef2 = coeffs[(predictorIdx * 2) + 1];

        short yn1 = hist1;
        short yn2 = hist2;

        for (int i = 0; i < 14; i++)
        {
            int byteIndex = 1 + (i / 2);
            int nibble;

            if (i % 2 == 0)
                nibble = (frame[byteIndex] >> 4) & 0x0F;
            else
                nibble = frame[byteIndex] & 0x0F;

            // Sign extend 4-bit nibble
            if (nibble >= 8)
                nibble -= 16;

            // Prediction
            // sample = (nibble << scale) + ((coef1 * yn1) + (coef2 * yn2)) >> 11
            // Note: Use long for intermediate calculation to avoid overflow before shift
            long prediction = ((long)coef1 * yn1) + ((long)coef2 * yn2);
            long sample = (nibble * scale) << 11; // Standard shift is 11 for format 0, but logic varies.

            // Standard Nintendo DSP logic:
            // val = (nibble << scale) << 11;
            // val += coef1 * hist1 + coef2 * hist2;
            // val >>= 11;

            long val = ((long)nibble * scale) << 11;
            val += prediction;
            long final = (val + 1024) >> 11; // Rounding (+1024 before shift)

            // Clamp
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
    /// Processes standard WAV/PCM/ADPCM RAKI formats and returns a WaveStream.
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

        // Build WAV in memory
        MemoryStream wavStream = new();
        using (BinaryWriter writer = new(wavStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteWavHeader(writer, dataSize, extraSize, extraData,
                compressionCode, channels, sampleRate, byteRate, blockAlign, bitsPerSample);

            stream.Position = startOffset;
            CopyAndSwapData(reader, writer, dataSize, isBigEndian, compressionCode, bitsPerSample);
        }

        wavStream.Position = 0;

        // Return a WaveFileReader that wraps the memory stream
        return new WaveFileReader(wavStream);
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
}

// --- Helper to convert Nintendo Switch Raw Opus -> Standard Ogg Opus ---
internal static class NxOpusToOggConverter
{
    private static readonly uint[] CrcTable = MakeCrcTable();

    public static void ConvertToOgg(BinaryReader reader, uint startOffset, long dataSize, Stream outputStream)
    {
        // Ensure we leave the output stream open, as we need to read from it later
        using BinaryWriter writer = new(outputStream, Encoding.UTF8, leaveOpen: true);

        reader.BaseStream.Position = startOffset;

        // 1. Read Nintendo Opus Header (Little Endian)
        // Nintendo Opus header format:
        // 0x00: Header magic/unknown
        // 0x10: Header inner size
        // 0x14: Unknown
        // 0x18: Pre-skip (uint32)
        // 0x1C: Output gain (int16) - in 1/256 dB units, needs to be applied during decoding
        reader.BaseStream.Seek(0x10, SeekOrigin.Current);
        uint headerInnerSize = reader.ReadUInt32();

        reader.BaseStream.Seek(0x08, SeekOrigin.Current);
        uint preSkip = reader.ReadUInt32();

        // Read output gain (affects volume level)
        short outputGainDb256 = reader.ReadInt16(); // Gain in 1/256 dB units
        float gainLinear = (float)Math.Pow(10.0, outputGainDb256 / (256.0 * 20.0)); // Convert dB to linear scale

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