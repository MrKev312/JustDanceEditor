using Concentus.Enums;
using Concentus.Structs;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Text;

namespace JustDanceEditor.Audio;

public static class RakiAudioEncoder
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
            {
                (audioBuffer[i + 1], audioBuffer[i]) = (audioBuffer[i], audioBuffer[i + 1]);
            }
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

    public static void EncodeToRakiCafeAdpcm(WaveStream source, Stream output, bool splitChannels = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        // Cafe uses 32000Hz for DSP ADPCM in Just Dance
        ISampleProvider resampled = source.ToSampleProvider();
        if (source.WaveFormat.SampleRate != 32000)
            resampled = new WdlResamplingSampleProvider(resampled, 32000);

        // Ensure Stereo
        if (resampled.WaveFormat.Channels != 2)
            resampled = resampled.ToMono().ToStereo();

        WaveStream pcmStream = new SampleProvider16ToWaveStreamAdapter(resampled, 32000, 2);
        byte[] pcmBytes = new byte[pcmStream.Length];
        pcmStream.ReadExactly(pcmBytes);

        int sampleCount = pcmBytes.Length / 4; // 2 bytes * 2 channels
        short[][] channelSamples = [new short[sampleCount], new short[sampleCount]];
        for (int i = 0; i < sampleCount; i++)
        {
            channelSamples[0][i] = BitConverter.ToInt16(pcmBytes, i * 4);
            channelSamples[1][i] = BitConverter.ToInt16(pcmBytes, (i * 4) + 2);
        }

        // Encode Channels
        byte[][] encodedData = new byte[2][];
        short[][] coefficients = new short[2][];
        for (int c = 0; c < 2; c++)
        {
            encodedData[c] = DspAdpcmEncoder.Encode(channelSamples[c], out coefficients[c]);
        }

        using BinaryWriter writer = new(output, Encoding.ASCII, leaveOpen: true);

        // Offsets & Sizes logic
        uint rakiHeaderSize = 32;

        // Split uses 5 chunks (fmt, dspL, dspR, datL, datR), Interleaved uses 4 (fmt, dspL, dspR, datS)
        uint chunkCount = splitChannels ? 5u : 4u;
        uint chunkTableSize = chunkCount * 12;

        uint fmtOffset = rakiHeaderSize + chunkTableSize;
        uint fmtSize = 18; // Fixed size for fmt 

        uint dspLOffset = fmtOffset + fmtSize;
        uint dspSize = 96; // Fixed size for dsp header

        uint dspROffset = dspLOffset + dspSize;

        // Data Start is aligned to 0x140 in both formats usually, 
        // effectively 0x20 alignment relative to the end of dspR
        uint dataStartOffset = dspROffset + dspSize;
        uint alignment = 0x20;
        uint remainder = dataStartOffset % alignment;
        if (remainder > 0)
        {
            dataStartOffset += alignment - remainder;
        }

        // Ensure we hit the standard 0x140 offset if the math allows (matches dumps)
        if (dataStartOffset < 0x140)
            dataStartOffset = 0x140;

        uint headerSize = dspROffset + dspSize; // Header Size covers up to end of chunks

        // Amb/Split format specifics based on Dump B
        // Version is 0x09 instead of 0x0B
        // Unk field is 0 instead of 3
        // Loop flag is usually enabled for Ambience
        uint version = splitChannels ? 9u : 0x0Bu;
        uint unkParam = splitChannels ? 0u : 3u;
        ushort loopFlag = (ushort)(splitChannels ? 1 : 0);

        // Write RAKI Header
        WriteRakiHeader(writer, "Cafe", "adpc", headerSize, dataStartOffset, chunkCount, unkParam, true, version);

        // Write Chunk Table
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        WriteU32(writer, fmtOffset, true);
        WriteU32(writer, fmtSize, true);

        writer.Write(Encoding.ASCII.GetBytes("dspL"));
        WriteU32(writer, dspLOffset, true);
        WriteU32(writer, dspSize, true);

        writer.Write(Encoding.ASCII.GetBytes("dspR"));
        WriteU32(writer, dspROffset, true);
        WriteU32(writer, dspSize, true);

        if (splitChannels)
        {
            uint datLSize = (uint)encodedData[0].Length;
            uint datRSize = (uint)encodedData[1].Length;

            // Right channel starts after Left channel, aligned to 0x20
            uint datLOffset = dataStartOffset;
            uint datROffset = datLOffset + datLSize;

            uint rRemainder = datROffset % 0x20;
            if (rRemainder > 0)
                datROffset += 0x20 - rRemainder;

            writer.Write(Encoding.ASCII.GetBytes("datL"));
            WriteU32(writer, datLOffset, true);
            WriteU32(writer, datLSize, true);

            writer.Write(Encoding.ASCII.GetBytes("datR"));
            WriteU32(writer, datROffset, true);
            WriteU32(writer, datRSize, true);
        }
        else
        {
            // Interleaved datS
            int blocks = encodedData[0].Length / 8;
            uint totalSize = (uint)(encodedData[0].Length + encodedData[1].Length);

            writer.Write(Encoding.ASCII.GetBytes("datS"));
            WriteU32(writer, dataStartOffset, true);
            WriteU32(writer, totalSize, true);
        }

        // Write fmt Chunk
        writer.BaseStream.Position = fmtOffset;
        WriteU16(writer, 2, true); // Format Code
        WriteU16(writer, 2, true); // Channels
        WriteU32(writer, 32000, true);
        WriteU32(writer, 64000, true); // Avg bytes
        WriteU16(writer, 2, true); // Block align
        WriteU16(writer, 16, true);
        WriteU16(writer, 0, true);

        // Write DSP Chunks
        for (int c = 0; c < 2; c++)
        {
            uint nibbleCount = (uint)encodedData[c].Length * 2;

            writer.BaseStream.Position = c == 0 ? dspLOffset : dspROffset;

            WriteU32(writer, (uint)sampleCount, true); // Word 0: Sample Count
            WriteU32(writer, nibbleCount, true);        // Word 1: Nibble Count (FIXED)
            WriteU32(writer, 32000, true);             // Word 2: Sample Rate

            WriteU16(writer, loopFlag, true);          // Word 3: Loop Flag
            WriteU16(writer, 0, true);                 // Word 3: Format (0 = ADPCM)

            WriteU32(writer, 0, true);                 // Word 4: Loop Start Address (Nibble 0)
            WriteU32(writer, nibbleCount - 1, true);   // Word 5: Loop End Address (Nibble count - 1) (FIXED)
            WriteU32(writer, 2, true);                 // Word 6: Current Address (Skip first byte)

            // Coefficients
            for (int i = 0; i < 16; i++)
                WriteU16(writer, (ushort)coefficients[c][i], true);

            WriteU16(writer, 0, true);                 // Gain

            // Initial Predictor/Scale (Must match the first byte of encoded data)
            writer.Write(encodedData[c][0]);
            writer.Write((byte)0);                     // Padding/Reserved

            WriteU16(writer, 0, true);                 // History 1
            WriteU16(writer, 0, true);                 // History 2
        }

        // Write Data
        writer.BaseStream.Position = dataStartOffset;

        if (splitChannels)
        {
            // Write Left
            writer.Write(encodedData[0]);

            // Pad alignment for Right
            long currentPos = writer.BaseStream.Position;
            long remainderPos = currentPos % 0x20;
            if (remainderPos > 0)
            {
                int pad = (int)(0x20 - remainderPos);
                writer.Write(new byte[pad]);
            }

            // Write Right
            writer.Write(encodedData[1]);
        }
        else
        {
            // Interleave logic (Original)
            int blocks = encodedData[0].Length / 8;
            byte[] interleavedData = new byte[encodedData[0].Length * 2];
            for (int i = 0; i < blocks; i++)
            {
                Array.Copy(encodedData[0], i * 8, interleavedData, i * 16, 8);
                Array.Copy(encodedData[1], i * 8, interleavedData, (i * 16) + 8, 8);
            }
            writer.Write(interleavedData);
        }
    }

    // UPDATED HELPER: Added 'version' parameter with default 0x0B
    private static void WriteRakiHeader(BinaryWriter writer, string platform, string type, uint headerSize, uint dataStartOffset, uint chunkCount, uint unk, bool isBigEndian, uint version = 0x0B)
    {
        writer.Write(Encoding.ASCII.GetBytes("RAKI"));
        WriteU32(writer, version, isBigEndian); // Now using the version parameter
        writer.Write(Encoding.ASCII.GetBytes(platform.PadRight(4).Substring(0, 4)));
        writer.Write(Encoding.ASCII.GetBytes(type.PadRight(4).Substring(0, 4)));
        WriteU32(writer, headerSize, isBigEndian);
        WriteU32(writer, dataStartOffset, isBigEndian);
        WriteU32(writer, chunkCount, isBigEndian);
        WriteU32(writer, unk, isBigEndian);
    }

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
                    Array.Clear(bufferFloat, samplesRead, bufferFloat.Length - samplesRead);
                for (int i = 0; i < bufferFloat.Length; i++)
                {
                    float f = bufferFloat[i];
                    bufferShort[i] = (short)(Math.Clamp(f, -1.0f, 1.0f) * 32767);
                }

                int packetLen = encoder.Encode(bufferShort, 0, frameSize, opusPacketBuffer, 0, opusPacketBuffer.Length);
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

    private static void WriteRakiHeader(BinaryWriter writer, string platform, string type, uint headerSize, uint dataStartOffset, uint chunkCount, uint unk, bool isBigEndian)
    {
        writer.Write(Encoding.ASCII.GetBytes("RAKI"));
        WriteU32(writer, 0x0B, isBigEndian);
        writer.Write(Encoding.ASCII.GetBytes(platform.PadRight(4).Substring(0, 4)));
        writer.Write(Encoding.ASCII.GetBytes(type.PadRight(4).Substring(0, 4)));
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

    private static bool CheckIsBigEndian(string platform) => platform.Trim() == "Wii" || platform.Trim() == "Cafe";

    private static class DspAdpcmEncoder
    {
        public static byte[] Encode(short[] pcmSamples, out short[] coefficients)
        {
            int sampleCount = pcmSamples.Length;
            int blockCount = (sampleCount + 13) / 14;
            byte[] encodedBuffer = new byte[blockCount * 8];
            coefficients = CalculateCoefficients(pcmSamples);

            for (int i = 0, d = 0; i < sampleCount; i += 14, d += 8)
            {
                int len = Math.Min(sampleCount - i, 14);
                short[] chunk = new short[16];
                chunk[0] = (i >= 2) ? pcmSamples[i - 2] : (short)0;
                chunk[1] = (i >= 1) ? pcmSamples[i - 1] : (short)0;
                for (int j = 0; j < len; j++)
                    chunk[j + 2] = pcmSamples[i + j];

                EncodeChunk(chunk, len, encodedBuffer, d, coefficients);
            }

            return encodedBuffer;
        }

        private static short[] CalculateCoefficients(short[] pcm)
        {
            int samples = pcm.Length;
            int numBlocks = (samples + 13) / 14;
            double[][] bufferArray = new double[8][];
            for (int i = 0; i < 8; i++)
                bufferArray[i] = new double[3];

            double[] sChunkBuffer = new double[3];
            double[] omgBuffer = new double[3];
            double[][] pChannels = new double[3][];
            for (int i = 0; i < 3; i++)
                pChannels[i] = new double[3];

            List<double[]> multiBuffer = [];
            short[] chunkBuffer = new short[28];

            for (int i = 0; i < samples; i += 14)
            {
                for (int z = 0; z < 14; z++)
                    chunkBuffer[z] = chunkBuffer[z + 14];
                for (int z = 0; z < 14; z++)
                    chunkBuffer[z + 14] = (i + z < samples) ? pcm[i + z] : (short)0;

                CalculateAutocorrelation(chunkBuffer, 14, sChunkBuffer);
                if (Math.Abs(sChunkBuffer[0]) > 10.0)
                {
                    CalculateCrossCorrelation(chunkBuffer, 14, pChannels);
                    if (!GaussianElimination(pChannels, out int[] pivot))
                    {
                        SolveSystem(pChannels, pivot, sChunkBuffer);
                        sChunkBuffer[0] = 1.0;
                        if (CalculateReflectionCoefficients(sChunkBuffer, omgBuffer) == 0)
                        {
                            double[] reflection = new double[3];
                            reflection[0] = 1.0;
                            for (int z = 1; z <= 2; z++)
                                reflection[z] = Math.Clamp(omgBuffer[z], -0.9999999999, 0.9999999999);
                            double[] lpc = new double[3];
                            ReflectionToLpc(reflection, lpc);
                            multiBuffer.Add(lpc);
                        }
                    }
                }
            }

            double[] avgLpc = [1.0, 0.0, 0.0];
            if (multiBuffer.Count > 0)
            {
                for (int i = 0; i < multiBuffer.Count; i++)
                {
                    LpcToAutocorrelation(multiBuffer[i], bufferArray[0]);
                    for (int y = 1; y <= 2; y++)
                        avgLpc[y] += bufferArray[0][y];
                }

                for (int y = 1; y <= 2; y++)
                    avgLpc[y] /= multiBuffer.Count;
            }

            LpcToReflection(avgLpc, omgBuffer, bufferArray[0], out _);
            for (int y = 1; y <= 2; y++)
                omgBuffer[y] = Math.Clamp(omgBuffer[y], -0.9999999999, 0.9999999999);
            ReflectionToLpc(omgBuffer, bufferArray[0]);

            for (int w = 0; w < 3; w++)
            {
                InitialCoefficientStep(bufferArray, bufferArray[0], 1 << w, 0.01);
                RefineCoefficients(bufferArray, 1 << (w + 1), multiBuffer);
            }

            short[] coeffs = new short[16];
            for (int z = 0; z < 8; z++)
            {
                for (int y = 0; y < 2; y++)
                {
                    double d = -bufferArray[z][y + 1] * 2048.0;
                    coeffs[(z * 2) + y] = (short)Math.Clamp(d > 0 ? d + 0.5 : d - 0.5, -32768, 32767);
                }
            }

            return coeffs;
        }

        private static void EncodeChunk(short[] source, int samples, byte[] dest, int destOff, short[] coefs)
        {
            double bestDist = double.MaxValue;
            int bestIdx = 0, bestScale = 0;
            int[] bestPcm = new int[14];

            for (int i = 0; i < 8; i++)
            {
                short c1 = coefs[i * 2], c2 = coefs[(i * 2) + 1];
                int s1 = source[1], s2 = source[0];
                int maxErr = 0;
                for (int s = 0; s < samples; s++)
                {
                    int pred = ((source[s + 1] * c1) + (source[s] * c2)) / 2048;
                    int err = source[s + 2] - pred;
                    if (Math.Abs(err) > Math.Abs(maxErr))
                        maxErr = err;
                }

                int scale = 0;
                while (scale <= 12 && (maxErr > 7 || maxErr < -8))
                {
                    scale++;
                    maxErr /= 2;
                }

                scale = Math.Max(0, scale);

                double totalDist = 0;
                int[] pcmOut = new int[14];
                int prev1 = source[1], prev2 = source[0];
                for (int s = 0; s < samples; s++)
                {
                    int pred = (prev1 * c1) + (prev2 * c2);
                    int rawErr = (source[s + 2] << 11) - pred;
                    int encoded = Math.Clamp((int)Math.Round((double)rawErr / (2048 * (1 << scale))), -8, 7);
                    pcmOut[s] = encoded;
                    int decoded = (pred + ((encoded * (1 << scale)) << 11) + 1024) >> 11;
                    decoded = Math.Clamp(decoded, -32768, 32767);
                    totalDist += Math.Pow(source[s + 2] - decoded, 2);
                    prev2 = prev1;
                    prev1 = decoded;
                }

                if (totalDist < bestDist)
                {
                    bestDist = totalDist;
                    bestIdx = i;
                    bestScale = scale;
                    bestPcm = pcmOut;
                }
            }

            dest[destOff] = (byte)((bestIdx << 4) | (bestScale & 0xF));
            for (int i = 0; i < 7; i++)
                dest[destOff + 1 + i] = (byte)(((bestPcm[i * 2] & 0xF) << 4) | (bestPcm[(i * 2) + 1] & 0xF));
        }

        private static void CalculateAutocorrelation(short[] src, int n, double[] dest)
        {
            for (int i = 0; i <= 2; i++)
            {
                dest[i] = 0;
                for (int j = i; j < n; j++)
                    dest[i] -= (double)src[j - i] * src[j];
            }
        }

        private static void CalculateCrossCorrelation(short[] src, int n, double[][] outList)
        {
            for (int x = 1; x <= 2; x++)
                for (int y = 1; y <= 2; y++)
                {
                    outList[x][y] = 0;
                    for (int z = 0; z < n; z++)
                        outList[x][y] += (double)src[z - x + 14] * src[z - y + 14];
                }
        }

        private static bool GaussianElimination(double[][] mat, out int[] pivot)
        {
            pivot = new int[3];
            double[] scales = new double[3];
            for (int i = 1; i <= 2; i++)
            {
                double max = 0;
                for (int j = 1; j <= 2; j++)
                    max = Math.Max(max, Math.Abs(mat[i][j]));
                if (max == 0)
                    return true;
                scales[i] = 1.0 / max;
            }

            for (int j = 1; j <= 2; j++)
            {
                for (int i = 1; i < j; i++)
                {
                    double sum = mat[i][j];
                    for (int k = 1; k < i; k++)
                        sum -= mat[i][k] * mat[k][j];
                    mat[i][j] = sum;
                }

                double max = 0;
                int p = j;
                for (int i = j; i <= 2; i++)
                {
                    double sum = mat[i][j];
                    for (int k = 1; k < j; k++)
                        sum -= mat[i][k] * mat[k][j];
                    mat[i][j] = sum;
                    if (Math.Abs(sum) * scales[i] >= max)
                    {
                        max = Math.Abs(sum) * scales[i];
                        p = i;
                    }
                }

                if (p != j)
                {
                    for (int k = 1; k <= 2; k++)
                        (mat[p][k], mat[j][k]) = (mat[j][k], mat[p][k]);
                    scales[p] = scales[j];
                }

                pivot[j] = p;
                if (mat[j][j] == 0)
                    return true;
                if (j != 2)
                {
                    double tmp = 1.0 / mat[j][j];
                    for (int i = j + 1; i <= 2; i++)
                        mat[i][j] *= tmp;
                }
            }

            return false;
        }

        private static void SolveSystem(double[][] mat, int[] pivot, double[] vec)
        {
            for (int i = 1, ii = 0; i <= 2; i++)
            {
                int ip = pivot[i];
                double sum = vec[ip];
                vec[ip] = vec[i];
                if (ii != 0)
                    for (int j = ii; j < i; j++)
                        sum -= mat[i][j] * vec[j];
                else if (sum != 0)
                    ii = i;
                vec[i] = sum;
            }

            for (int i = 2; i >= 1; i--)
            {
                double sum = vec[i];
                for (int j = i + 1; j <= 2; j++)
                    sum -= mat[i][j] * vec[j];
                vec[i] = sum / mat[i][i];
            }
        }

        private static int CalculateReflectionCoefficients(double[] lpc, double[] refl)
        {
            double v2 = lpc[2];
            double tmp = 1.0 - (v2 * v2);
            if (tmp == 0)
                return 1;
            refl[1] = (lpc[1] - (lpc[1] * v2)) / tmp;
            refl[2] = v2;
            return Math.Abs(refl[1]) > 1.0 ? 1 : 0;
        }

        private static void ReflectionToLpc(double[] refl, double[] lpc)
        {
            lpc[0] = 1.0;
            lpc[1] = (refl[2] * refl[1]) + refl[1];
            lpc[2] = refl[2];
        }

        private static void LpcToAutocorrelation(double[] lpc, double[] auto)
        {
            double[][] work = new double[3][];
            for (int i = 0; i < 3; i++)
                work[i] = new double[3];
            for (int i = 1; i <= 2; i++)
                work[2][i] = -lpc[i];
            for (int i = 2; i > 0; i--)
            {
                double denom = 1.0 - (work[i][i] * work[i][i]);
                for (int j = 1; j < i; j++)
                    work[i - 1][j] = ((work[i][i] * work[i][j]) + work[i][j]) / denom;
            }

            auto[0] = 1.0;
            for (int i = 1; i <= 2; i++)
            {
                auto[i] = 0;
                for (int j = 1; j <= i; j++)
                    auto[i] += work[i][j] * auto[i - j];
            }
        }

        private static int LpcToReflection(double[] lpc, double[] refl, double[] workLpc, out double finalAuto)
        {
            int bad = 0;
            double auto = lpc[0];
            workLpc[0] = 1.0;
            for (int i = 1; i <= 2; i++)
            {
                double sum = 0;
                for (int j = 1; j < i; j++)
                    sum += workLpc[j] * lpc[i - j];
                workLpc[i] = (auto > 0) ? -(sum + lpc[i]) / auto : 0;
                refl[i] = workLpc[i];
                if (Math.Abs(refl[i]) > 1.0)
                    bad++;
                for (int j = 1; j < i; j++)
                    workLpc[j] += workLpc[i] * workLpc[i - j];
                auto *= 1.0 - (workLpc[i] * workLpc[i]);
            }

            finalAuto = auto;
            return bad;
        }

        private static void InitialCoefficientStep(double[][] coeffs, double[] vec, int n, double step)
        {
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= 2; j++)
                    coeffs[n + i][j] = (step * vec[j]) + coeffs[i][j];
        }

        private static void RefineCoefficients(double[][] coeffs, int n, List<double[]> multi)
        {
            double[][] newList = new double[n][];
            int[] counts = new int[n];
            for (int i = 0; i < n; i++)
                newList[i] = new double[3];

            for (int x = 0; x < 2; x++)
            {
                Array.Clear(counts, 0, n);
                for (int i = 0; i < n; i++)
                    Array.Clear(newList[i], 0, 3);
                foreach (double[] lpc in multi)
                {
                    int best = 0;
                    double minErr = 1e30;
                    for (int i = 0; i < n; i++)
                    {
                        double err = CalculateLpcError(coeffs[i], lpc);
                        if (err < minErr)
                        {
                            minErr = err;
                            best = i;
                        }
                    }

                    counts[best]++;
                    double[] auto = new double[3];
                    LpcToAutocorrelation(lpc, auto);
                    for (int j = 0; j <= 2; j++)
                        newList[best][j] += auto[j];
                }

                for (int i = 0; i < n; i++)
                {
                    if (counts[i] > 0)
                        for (int j = 0; j <= 2; j++)
                            newList[i][j] /= counts[i];
                    double[] refl = new double[3], work = new double[3];
                    LpcToReflection(newList[i], refl, work, out _);
                    for (int j = 1; j <= 2; j++)
                        refl[j] = Math.Clamp(refl[j], -0.9999999999, 0.9999999999);
                    ReflectionToLpc(refl, coeffs[i]);
                }
            }
        }

        private static double CalculateLpcError(double[] c1, double[] c2)
        {
            double v2 = -c2[1], v3 = -c2[2];
            double val = ((v3 * v2) + v2) / (1.0 - (v3 * v3));
            double[] b = [1.0, val, (v2 * val) + v3];
            double r1 = (c1[0] * c1[0]) + (c1[1] * c1[1]) + (c1[2] * c1[2]);
            double r2 = (c1[0] * c1[1]) + (c1[1] * c1[2]);
            double r3 = c1[0] * c1[2];
            return (b[0] * r1) + (2.0 * b[1] * r2) + (2.0 * b[2] * r3);
        }
    }

    private sealed class SampleProvider16ToWaveStreamAdapter : WaveStream
    {
        private readonly WaveFormat _waveFormat;
        private readonly byte[] _audioBuffer;
        private long _position = 0;

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
        public override long Position { get => _position; set => _position = value; }
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