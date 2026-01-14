using Concentus.Enums;
using Concentus.Structs;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static class RakiAudioEncoder
{
    // PCM and ADPCM methods remain unchanged
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
        ushort compressionCode = format.Encoding == WaveFormatEncoding.Pcm ? (ushort)1 : (ushort)2;

        // Swap endianness if necessary (e.g. for Wii/Cafe/PS3)
        // Nx (Switch) is Little Endian, so this usually won't run for Nx.
        if (isBigEndian && compressionCode == 1 && format.BitsPerSample == 16)
        {
            for (int i = 0; i < bytesRead; i += 2)
            {
                (audioBuffer[i + 1], audioBuffer[i]) = (audioBuffer[i], audioBuffer[i + 1]);
            }
        }

        // --- Calculate Structure Layout ---

        // 1. RAKI Header: 32 bytes
        uint rakiHeaderSize = 32;

        // 2. Chunk Table: 2 entries (fmt + data) * 12 bytes = 24 bytes
        uint chunkTableSize = 24;

        // 3. Current Offset (End of Chunk Table) -> 32 + 24 = 56 (0x38)
        uint currentOffset = rakiHeaderSize + chunkTableSize;

        // 4. fmt Chunk
        uint fmtOffset = currentOffset;
        uint fmtSize = 16; // Standard PCM size (no extra bytes)
        currentOffset += fmtSize;

        // 5. data Chunk
        // The hex dump provided shows tightly packed data (Offset 72 / 0x48).
        // Standard PCM usually doesn't strictly require the 16-byte alignment that Opus does,
        // but if you notice issues, you can re-add the alignment logic here.
        uint dataOffset = currentOffset;
        uint dataSize = (uint)bytesRead;

        // 6. Final Header Size (Points to where data starts)
        uint totalHeaderSize = dataOffset;

        // --- Write Data ---

        // RAKI Header
        WriteRakiHeader(writer, platform, type, totalHeaderSize, dataOffset, 2, 0, isBigEndian);

        // Chunk Table: fmt
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, fmtOffset, isBigEndian);
        WriteU32(writer, fmtSize, isBigEndian);

        // Chunk Table: data
        writer.Write(Encoding.ASCII.GetBytes("data"));
        WriteU32(writer, dataOffset, isBigEndian);
        WriteU32(writer, dataSize, isBigEndian);

        // fmt Chunk Data
        // We are now at offset 0x38 (56), exactly where the table said we'd be.
        WriteU16(writer, compressionCode, isBigEndian);
        WriteU16(writer, (ushort)format.Channels, isBigEndian);
        WriteU32(writer, (uint)format.SampleRate, isBigEndian);
        WriteU32(writer, (uint)format.AverageBytesPerSecond, isBigEndian);
        WriteU16(writer, (ushort)format.BlockAlign, isBigEndian);
        WriteU16(writer, (ushort)format.BitsPerSample, isBigEndian);
        // Note: No extra 4 bytes of padding written here, matching the file size 16.

        // Audio Data
        // We are now at offset 0x48 (72)
        writer.Write(audioBuffer, 0, bytesRead);
    }

    public static void EncodeToRakiAdpcm(WaveStream source, Stream output, string platform = "Win ")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);
        byte[] audioBuffer = new byte[source.Length];
        source.Position = 0;
        int bytesRead = source.Read(audioBuffer, 0, audioBuffer.Length);
        WaveFormat format = source.WaveFormat;
        byte[] adpcmData = EncodeToAdpcm(audioBuffer, format);

        ushort blockAlign = (ushort)((format.Channels * 2) + 2);
        uint byteRate = (uint)(blockAlign * format.SampleRate / 256);
        uint headerSize = 0x40;
        uint dataStartOffset = headerSize;
        bool isBigEndian = CheckIsBigEndian(platform);

        WriteRakiHeader(writer, platform, "adpc", headerSize, dataStartOffset, 2, 0, isBigEndian);

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, 0x2C, isBigEndian);
        WriteU32(writer, 20, isBigEndian);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        WriteU32(writer, dataStartOffset, isBigEndian);
        WriteU32(writer, (uint)adpcmData.Length, isBigEndian);

        while (writer.BaseStream.Position < 0x2C)
            writer.Write((byte)0);

        WriteU16(writer, 2, isBigEndian);
        WriteU16(writer, (ushort)format.Channels, isBigEndian);
        WriteU32(writer, (uint)format.SampleRate, isBigEndian);
        WriteU32(writer, byteRate, isBigEndian);
        WriteU16(writer, blockAlign, isBigEndian);
        WriteU16(writer, 16, isBigEndian);
        WriteU16(writer, 2, isBigEndian);
        WriteU16(writer, 0, isBigEndian);

        while (writer.BaseStream.Position < dataStartOffset)
            writer.Write((byte)0);

        writer.Write(adpcmData);
    }

    public static void EncodeToRakiNxOpus(WaveStream source, Stream output, IList<int>? markers = null, uint preSkip = 120, short outputGainDb256 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        // 1. Configure Audio Source (48kHz Stereo)
        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (source.WaveFormat.SampleRate != 48000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);
        }

        // Ensure Stereo (Opus requirement for this specific container format usually)
        if (sampleProvider.WaveFormat.Channels != 2)
        {
            if (sampleProvider.WaveFormat.Channels == 1)
                sampleProvider = sampleProvider.ToStereo();
            else
                sampleProvider = sampleProvider.ToMono().ToStereo(); // Downmix then stereo
        }

        // 2. Setup Concentus Encoder directly
        OpusEncoder encoder = new(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO)
        {
            Bitrate = 192000,
            ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_20_MS
        };

        const int frameSize = 960; // 20ms @ 48kHz
        float[] bufferFloat = new float[frameSize * 2];
        short[] bufferShort = new short[frameSize * 2];
        byte[] opusPacketBuffer = new byte[1275]; // Max standard opus packet size

        MemoryStream payloadStream = new();
        long totalOpusSamples = 0;

        using (BinaryWriter payloadWriter = new(payloadStream, Encoding.ASCII, leaveOpen: true))
        {
            int samplesRead;
            while ((samplesRead = sampleProvider.Read(bufferFloat, 0, bufferFloat.Length)) > 0)
            {
                // Pad with zeros if partial frame
                if (samplesRead < bufferFloat.Length)
                {
                    Array.Clear(bufferFloat, samplesRead, bufferFloat.Length - samplesRead);
                }

                // Convert float to short for Concentus
                for (int i = 0; i < bufferFloat.Length; i++)
                {
                    float f = bufferFloat[i];
                    if (f > 1.0f)
                        f = 1.0f;
                    else if (f < -1.0f)
                        f = -1.0f;
                    bufferShort[i] = (short)(f * 32767);
                }

                // Encode
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
                    // --- WRITE NX PACKET ---
                    // [Size (4B BE)] [FinalRange (4B BE)] [OpusData]

                    WriteU32BE(payloadWriter, (uint)packetLen);
                    WriteU32BE(payloadWriter, encoder.FinalRange); // FinalRange is the 'Unknown' field
                    payloadWriter.Write(opusPacketBuffer, 0, packetLen);

                    totalOpusSamples += frameSize;
                }
            }
        }

        byte[] nxOpusData = payloadStream.ToArray();
        payloadStream.Dispose();

        // 4. Construct Nx Opus Header
        uint headerInnerSize = 0x20;
        MemoryStream headerStream = new();
        using (BinaryWriter nxWriter = new(headerStream, Encoding.UTF8, leaveOpen: true))
        {
            nxWriter.Write(0x80000001);                 // 0x00 Magic
            nxWriter.Write((uint)0x18);                 // 0x04 Size

            // FIX: Channels are shifted by 8 bits (e.g. 2 -> 0x200)
            uint channels = (uint)sampleProvider.WaveFormat.Channels;
            nxWriter.Write(channels << 8);              // 0x08 Channels (00 02 00 00)

            nxWriter.Write((uint)48000);                // 0x0C Sample Rate
            nxWriter.Write(headerInnerSize);            // 0x10 Inner Size
            nxWriter.Write(0u);                         // 0x14 Unknown
            nxWriter.Write(0u);                         // 0x18 Unknown
            nxWriter.Write(preSkip);                    // 0x1C PreSkip
            nxWriter.Write(outputGainDb256);            // 0x20 Gain

            // FIX: Padding/Flags must match official file (00 80)
            nxWriter.Write((byte)0x00);                 // 0x22
            nxWriter.Write((byte)0x80);                 // 0x23 (Matches official dump)

            // 0x24: Payload Size
            nxWriter.Write((uint)nxOpusData.Length);

            nxWriter.Write(nxOpusData);
        }

        byte[] fullNxData = headerStream.ToArray();
        headerStream.Dispose();

        // 5. Construct RAKI Header
        bool hasMarkers = markers != null && markers.Count > 0;
        List<(string magic, uint offset, uint size)> chunks = [];

        uint chunkTableStart = 0x20;
        uint chunkTableSize = hasMarkers ? 60u : 36u;
        uint currentDataOffset = chunkTableStart + chunkTableSize;

        // fmt
        uint fmtDataOffset = currentDataOffset;
        uint fmtDataSize = 0x10;
        chunks.Add(("fmt ", fmtDataOffset, fmtDataSize));
        currentDataOffset += fmtDataSize;

        // MARK
        byte[]? markData = null;
        if (hasMarkers)
        {
            markData = GenerateMarkChunk(markers!);
            uint markSize = (uint)markData.Length;
            chunks.Add(("MARK", currentDataOffset, markSize));
            currentDataOffset += markSize;
        }

        // STRG
        byte[]? strgData = null;
        if (hasMarkers)
        {
            strgData = GenerateStrgChunk(markers!);
            uint strgSize = (uint)strgData.Length;
            chunks.Add(("STRG", currentDataOffset, strgSize));
            currentDataOffset += strgSize;
        }

        // AdIn
        uint adInDataOffset = currentDataOffset;
        uint adInDataSize = 0x04;
        chunks.Add(("AdIn", adInDataOffset, adInDataSize));
        currentDataOffset += adInDataSize;

        // Header Size
        uint headerSize = currentDataOffset;

        // Data Start - ALIGNMENT FIX (16 bytes)
        uint alignment = 0x10;
        uint remainder = headerSize % alignment;
        uint padding = remainder > 0 ? alignment - remainder : 0;
        uint dataStartOffset = headerSize + padding;

        // data
        uint dataSize = (uint)fullNxData.Length;
        chunks.Add(("data", dataStartOffset, dataSize));

        // Write Header
        WriteRakiHeader(writer, "Nx  ", "Nx  ", headerSize, dataStartOffset, (uint)chunks.Count, 3, false);

        // Write Chunk Table
        foreach ((string magic, uint offset, uint size) in chunks)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(offset);
            writer.Write(size);
        }

        // Write Chunk Data
        // fmt
        WriteU16(writer, 0x0063, false);
        WriteU16(writer, 2, false); // Channels
        WriteU32(writer, 48000, false);
        WriteU32(writer, 192000, false);
        WriteU16(writer, 4, false);
        WriteU16(writer, 16, false);

        // MARK
        if (hasMarkers && markData != null)
            writer.Write(markData);

        // STRG
        if (hasMarkers && strgData != null)
            writer.Write(strgData);

        // AdIn
        // This MUST match the total decoded samples
        writer.Write((uint)totalOpusSamples);

        // Padding to Data Offset
        while (writer.BaseStream.Position < dataStartOffset)
        {
            writer.Write((byte)0);
        }

        // data (Nx Opus Header + Packets)
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

    private static bool CheckIsBigEndian(string platform) => platform switch
    {
        "Wii " => true,
        "Cafe" => true,
        "PS3 " => true,
        "X360" => true,
        _ => false
    };

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

    private static byte[] EncodeToAdpcm(byte[] pcm, WaveFormat fmt) => new byte[pcm.Length];

    /// <summary>
    /// Adapter that wraps an ISampleProvider (float samples) into a WaveStream (16-bit PCM) format.
    /// Buffers all audio data in memory for compatibility with audio encoding operations.
    /// </summary>
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

        /// <summary>
        /// Converts all samples from the ISampleProvider to 16-bit PCM format.
        /// </summary>
        private byte[] ConvertSamplesToPcm16(ISampleProvider sampleProvider)
        {
            const int bufferSize = 4096;
            float[] sampleBuffer = new float[bufferSize * _waveFormat.Channels];
            List<byte> pcmData = new();

            int samplesRead;
            while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
            {
                // Convert float samples [-1.0, 1.0] to 16-bit PCM
                for (int i = 0; i < samplesRead; i++)
                {
                    float sample = sampleBuffer[i];
                    // Clamp to [-1.0, 1.0] and convert to 16-bit little-endian
                    short pcm16 = (short)Math.Max(-32768, Math.Min(32767, sample * 32767.0f));
                    
                    pcmData.Add((byte)(pcm16 & 0xFF));
                    pcmData.Add((byte)((pcm16 >> 8) & 0xFF));
                }
            }

            return pcmData.ToArray();
        }
    }
}