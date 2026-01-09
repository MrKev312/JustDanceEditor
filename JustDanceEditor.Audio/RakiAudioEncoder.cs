using NAudio.Wave;

using System.Text;

namespace JustDanceEditor.Audio;

/// <summary>
/// Encodes WaveStream audio data to RAKI format (PCM and Windows ADPCM variants).
/// Used primarily for round-trip testing and format conversion.
/// </summary>
public static class RakiAudioEncoder
{
    /// <summary>
    /// Encodes a WaveStream to RAKI PCM format.
    /// </summary>
    public static void EncodeToRakiPcm(WaveStream source, Stream output, string platform = "Win ", string type = "pcm ")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        bool isBigEndian = CheckIsBigEndian(platform);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        // Read audio data
        byte[] audioBuffer = new byte[source.Length];
        source.Position = 0;
        int bytesRead = source.Read(audioBuffer, 0, audioBuffer.Length);

        // Get format info
        WaveFormat format = source.WaveFormat;
        ushort compressionCode = format.Encoding == WaveFormatEncoding.Pcm ? (ushort)1 : (ushort)2;

        // Swap bytes for big endian PCM if needed
        if (isBigEndian && compressionCode == 1 && format.BitsPerSample == 16)
        {
            for (int i = 0; i < bytesRead; i += 2)
            {
                (audioBuffer[i + 1], audioBuffer[i]) = (audioBuffer[i], audioBuffer[i + 1]);
            }
        }

        // Build RAKI header
        uint headerSize = 0x40; // Standard header size
        uint dataStartOffset = headerSize;

        WriteRakiHeader(writer, platform, type, headerSize, dataStartOffset, isBigEndian);
        WriteFmtChunk(writer, format, compressionCode, isBigEndian);
        WriteAudioData(writer, audioBuffer, bytesRead);
    }

    /// <summary>
    /// Encodes a WaveStream to RAKI Windows ADPCM format.
    /// </summary>
    public static void EncodeToRakiAdpcm(WaveStream source, Stream output, string platform = "Win ")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        // Read audio data
        byte[] audioBuffer = new byte[source.Length];
        source.Position = 0;
        int bytesRead = source.Read(audioBuffer, 0, audioBuffer.Length);

        WaveFormat format = source.WaveFormat;

        // Encode PCM to ADPCM
        byte[] adpcmData = EncodeToAdpcm(audioBuffer, format);

        // Build RAKI header with ADPCM specifics
        ushort blockAlign = (ushort)(format.Channels * 2 + 2); // 2 bytes per sample + 2 bytes header per channel
        uint byteRate = (uint)(blockAlign * format.SampleRate / 256); // Typical ADPCM rate
        uint headerSize = 0x40;
        uint dataStartOffset = headerSize;

        bool isBigEndian = CheckIsBigEndian(platform);

        WriteRakiHeader(writer, platform, "adpc", headerSize, dataStartOffset, isBigEndian);

        // Write ADPCM fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        uint fmtSize = 20; // ADPCM fmt size (includes cbSize)
        WriteU32(writer, fmtSize, isBigEndian);
        WriteU16(writer, 2, isBigEndian); // ADPCM compression code
        WriteU16(writer, (ushort)format.Channels, isBigEndian);
        WriteU32(writer, (uint)format.SampleRate, isBigEndian);
        WriteU32(writer, byteRate, isBigEndian);
        WriteU16(writer, blockAlign, isBigEndian);
        WriteU16(writer, 16, isBigEndian); // Bits per sample
        WriteU16(writer, 2, isBigEndian); // Extra data size

        WriteAudioData(writer, adpcmData, adpcmData.Length);
    }

    /// <summary>
    /// Encodes a WaveStream to RAKI Nintendo Switch Opus format.
    /// Resamples to 48kHz if needed, encodes to Opus, and wraps in RAKI container.
    /// </summary>
    public static void EncodeToRakiNxOpus(WaveStream source, Stream output, uint preSkip = 0, short outputGainDb256 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        // Convert to Opus using OpusEncoderHelper
        MemoryStream opusStream = new();
        ISampleProvider sampleProvider = source.ToSampleProvider();
        OpusEncoderHelper.EncodeToOpus(sampleProvider, opusStream, 128000);

        byte[] opusData = opusStream.ToArray();
        opusStream.Dispose();

        // Build Nintendo Opus header
        uint headerSize = 0x30; // Nintendo Opus header size
        uint headerInnerSize = 0x20; // Inner header size

        MemoryStream nxOpusStream = new();
        using (BinaryWriter nxWriter = new(nxOpusStream, Encoding.UTF8, leaveOpen: true))
        {
            // Write Nintendo Opus header
            nxWriter.Write(new byte[0x10]); // 0x00-0x0F: Header magic/unknown
            nxWriter.Write(headerInnerSize); // 0x10: Inner size (LE)
            nxWriter.Write(new byte[0x08]); // 0x14-0x1B: Unknown/padding
            nxWriter.Write(preSkip); // 0x1C: Pre-skip (LE)
            nxWriter.Write(outputGainDb256); // 0x20: Output gain in 1/256 dB (LE)
            nxWriter.Write(new byte[0x0C]); // 0x22-0x2D: Padding to align to 0x30

            // Write Opus packets
            // Parse Ogg Opus to extract raw packets
            ParseAndWriteOpusPackets(opusData, nxWriter);
        }

        byte[] nxOpusData = nxOpusStream.ToArray();
        nxOpusStream.Dispose();

        // Write RAKI container
        uint dataStartOffset = headerSize;
        WriteRakiHeader(writer, "Nx  ", "Nx  ", headerSize, dataStartOffset, false);
        WriteAudioData(writer, nxOpusData, nxOpusData.Length);
    }

    /// <summary>
    /// Parses an Ogg Opus stream and extracts raw Opus packets in Nintendo format.
    /// </summary>
    private static void ParseAndWriteOpusPackets(byte[] oggData, BinaryWriter writer)
    {
        // Simple Ogg parser - extract packets from Ogg page structure
        // Ogg pages start with "OggS" (0x4F 0x67 0x67 0x53)
        // This is a simplified implementation for Ogg Opus files

        int position = 0;
        while (position < oggData.Length - 27) // Minimum page size
        {
            // Look for Ogg page header
            if (oggData[position] == 0x4F && oggData[position + 1] == 0x67 &&
                oggData[position + 2] == 0x67 && oggData[position + 3] == 0x53)
            {
                // Skip to segment table
                int numSegments = oggData[position + 26];
                int segmentTableStart = position + 27;
                int payloadStart = segmentTableStart + numSegments;

                // Write each segment as a Nintendo Opus packet
                int payloadPos = payloadStart;
                for (int i = 0; i < numSegments && payloadPos < oggData.Length; i++)
                {
                    int segmentSize = oggData[segmentTableStart + i];
                    if (payloadPos + segmentSize <= oggData.Length)
                    {
                        // Write packet: [Size (4B BE)] [Unknown (4B)] [Opus Data]
                        byte[] sizeBytes = BitConverter.GetBytes((uint)segmentSize);
                        Array.Reverse(sizeBytes); // Convert to big-endian
                        writer.Write(sizeBytes);
                        writer.Write(new byte[4]); // Unknown field (zeros)
                        writer.Write(oggData, payloadPos, segmentSize);

                        payloadPos += segmentSize;
                    }
                }

                // Move to next page
                position = payloadStart + numSegments; // Rough estimate, should parse segment sizes properly
                // For now, skip to next OggS marker or end
                while (position < oggData.Length - 3)
                {
                    if (oggData[position] == 0x4F && oggData[position + 1] == 0x67 &&
                        oggData[position + 2] == 0x67 && oggData[position + 3] == 0x53)
                        break;
                    position++;
                }
            }
            else
            {
                position++;
            }
        }
    }

    private static void WriteRakiHeader(BinaryWriter writer, string platform, string type, uint headerSize, uint dataStartOffset, bool isBigEndian)
    {
        writer.Write(Encoding.ASCII.GetBytes("RAKI"));
        WriteU32(writer, 0x10, isBigEndian); // Header inner size
        writer.Write(Encoding.ASCII.GetBytes(platform));
        writer.Write(Encoding.ASCII.GetBytes(type));
        WriteU32(writer, headerSize, isBigEndian);
        WriteU32(writer, dataStartOffset, isBigEndian);
        WriteU32(writer, 1, isBigEndian); // Unknown field
        WriteU32(writer, 1, isBigEndian); // Chunk entry count

        // Chunk entry: fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, 0x2C, isBigEndian); // fmt chunk offset (from start)

        // Padding to header size (0x40)
        long currentPos = writer.BaseStream.Position;
        long bytesToPad = headerSize - currentPos;
        for (int i = 0; i < bytesToPad; i++)
            writer.Write((byte)0);
    }

    private static void WriteFmtChunk(BinaryWriter writer, WaveFormat format, ushort compressionCode, bool isBigEndian)
    {
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        uint chunkSize = 16;
        WriteU32(writer, chunkSize, isBigEndian);
        WriteU16(writer, compressionCode, isBigEndian);
        WriteU16(writer, (ushort)format.Channels, isBigEndian);
        WriteU32(writer, (uint)format.SampleRate, isBigEndian);
        WriteU32(writer, (uint)format.AverageBytesPerSecond, isBigEndian);
        WriteU16(writer, (ushort)format.BlockAlign, isBigEndian);
        WriteU16(writer, (ushort)format.BitsPerSample, isBigEndian);
    }

    private static void WriteAudioData(BinaryWriter writer, byte[] audioBuffer, int length)
    {
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((uint)length);
        writer.Write(audioBuffer, 0, length);
    }

    /// <summary>
    /// Simple PCM to ADPCM encoder (basic MS-ADPCM style).
    /// </summary>
    private static byte[] EncodeToAdpcm(byte[] pcmData, WaveFormat format)
    {
        // Simplified ADPCM encoding - convert PCM samples to ADPCM nibbles
        // For testing purposes, we can use a simplified approach

        int sampleCount = pcmData.Length / (format.BitsPerSample / 8) / format.Channels;
        int blockSize = 256; // Samples per block

        List<byte> adpcmData = [];

        for (int i = 0; i < sampleCount; i += blockSize)
        {
            int samplesToEncode = Math.Min(blockSize, sampleCount - i);
            byte[] block = EncodeAdpcmBlock(pcmData, format, i * format.BlockAlign, samplesToEncode);
            adpcmData.AddRange(block);
        }

        return adpcmData.ToArray();
    }

    private static byte[] EncodeAdpcmBlock(byte[] pcmData, WaveFormat format, int offset, int sampleCount)
    {
        // Placeholder ADPCM block encoding
        // For real implementation, use proper ADPCM algorithm
        List<byte> block =
        [
            // Write a simplified block header (predictor index and delta)
            0, // Predictor index
            0, // Delta
        ];

        // Encode samples as nibbles (simplified)
        for (int i = 0; i < sampleCount; i++)
        {
            // Just copy/quantize samples for now
            block.Add(0);
        }

        return block.ToArray();
    }

    private static bool CheckIsBigEndian(string platform) => platform switch
    {
        "Wii " => true,
        "Cafe" => true,
        "PS3 " => true,
        "X360" => true,
        _ => false
    };

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