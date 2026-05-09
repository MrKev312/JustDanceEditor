using NAudio.Wave;

using System.Numerics;
using System.Text;

namespace JustDanceEditor.Audio.Codecs;

internal static class Xma2Decoder
{
    private const int PacketSize = 2048;
    private const int SamplesPerFrame = 512;
    private const int MaxChannelsPerStream = 2;

    public static WaveStream DecodeToWaveStream(BinaryReader reader, uint fmtOffset, uint dataOffset, long dataSize, bool isBigEndian)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Xma2Format format = Xma2Format.Read(reader, fmtOffset, isBigEndian);
        if (format.FormatTag != 0x0166)
            throw new InvalidDataException($"Expected XMA2 format tag 0x0166, got 0x{format.FormatTag:X4}.");
        if (format.Channels <= 0)
            throw new InvalidDataException("XMA2 stream has no channels.");
        if (format.Channels > 16)
            throw new NotSupportedException($"XMA2 streams with {format.Channels} channels are not supported.");

        reader.BaseStream.Position = dataOffset;
        byte[] xmaData = reader.ReadBytes(checked((int)dataSize));

        Xma2PacketDecoder decoder = new(format);
        short[] samples = decoder.Decode(xmaData);

        MemoryStream wavStream = new();
        using (BinaryWriter writer = new(wavStream, Encoding.ASCII, leaveOpen: true))
        {
            long pcmBytes = samples.LongLength * sizeof(short);
            WritePcmWaveHeader(writer, pcmBytes, format.Channels, format.SampleRate);
            foreach (short sample in samples)
                writer.Write(sample);
        }

        wavStream.Position = 0;
        return new WaveFileReader(wavStream);
    }

    private static void WritePcmWaveHeader(BinaryWriter writer, long dataSize, int channels, int sampleRate)
    {
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked((uint)(36 + dataSize)));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write((uint)sampleRate);
        writer.Write((uint)(sampleRate * channels * sizeof(short)));
        writer.Write((ushort)(channels * sizeof(short)));
        writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(checked((uint)dataSize));
    }

    private readonly record struct Xma2Format(
        ushort FormatTag,
        int Channels,
        int SampleRate,
        int BitsPerSample,
        int NumStreams,
        int SamplesEncoded)
    {
        public static Xma2Format Read(BinaryReader reader, uint fmtOffset, bool isBigEndian)
        {
            reader.BaseStream.Position = fmtOffset;
            ushort formatTag = ReadU16(reader, isBigEndian);
            int channels = ReadU16(reader, isBigEndian);
            int sampleRate = checked((int)ReadU32(reader, isBigEndian));
            _ = ReadU32(reader, isBigEndian);
            _ = ReadU16(reader, isBigEndian);
            int bitsPerSample = ReadU16(reader, isBigEndian);
            ushort extraSize = ReadU16(reader, isBigEndian);

            int numStreams = Math.Max(1, (channels + 1) / 2);
            int samplesEncoded = 0;

            if (extraSize >= 0x22)
            {
                numStreams = ReadU16(reader, isBigEndian);
                _ = ReadU32(reader, isBigEndian);
                samplesEncoded = checked((int)ReadU32(reader, isBigEndian));
            }

            return new Xma2Format(formatTag, channels, sampleRate, bitsPerSample, numStreams, samplesEncoded);
        }
    }

    private sealed class Xma2PacketDecoder
    {
        private readonly Xma2Format _format;
        private readonly WmaProStreamDecoder[] _streams;
        private readonly SampleQueue[,] _queuedSamples;
        private readonly int[] _startChannel;
        private readonly int[] _channelStream;
        private readonly int[] _channelLocal;
        private int _currentStream;

        public Xma2PacketDecoder(Xma2Format format)
        {
            _format = format;
            int numStreams = Math.Clamp(format.NumStreams, 1, (format.Channels + 1) / 2);
            _streams = new WmaProStreamDecoder[numStreams];
            _queuedSamples = new SampleQueue[MaxChannelsPerStream, numStreams];
            _startChannel = new int[numStreams];
            _channelStream = new int[format.Channels];
            _channelLocal = new int[format.Channels];

            int startChannel = 0;
            for (int i = 0; i < numStreams; i++)
            {
                int channelsInStream = Math.Min(MaxChannelsPerStream, format.Channels - startChannel);
                if (channelsInStream <= 0)
                    throw new InvalidDataException("Invalid XMA2 stream/channel layout.");

                _streams[i] = new WmaProStreamDecoder(format.SampleRate, channelsInStream);
                _startChannel[i] = startChannel;

                for (int c = 0; c < channelsInStream; c++)
                {
                    _channelStream[startChannel + c] = i;
                    _channelLocal[startChannel + c] = c;
                }

                startChannel += channelsInStream;

                _queuedSamples[0, i] = new SampleQueue();
                _queuedSamples[1, i] = new SampleQueue();
            }

            if (startChannel != format.Channels)
                throw new InvalidDataException("XMA2 stream/channel layout does not cover every channel.");
        }

        public short[] Decode(byte[] data)
        {
            int targetSamples = _format.SamplesEncoded > 0
                ? _format.SamplesEncoded * _format.Channels
                : 0;

            int estimatedSamples = targetSamples > 0
                ? targetSamples
                : (data.Length / PacketSize) * SamplesPerFrame * _format.Channels;

            List<short> pcm = new(estimatedSamples);
            int packetCount = data.Length / PacketSize;

            if (_streams.Length == 1)
            {
                DecodeSingleStream(data, packetCount, pcm);
            }
            else
            {
                DecodeInterleavedStreams(data, packetCount, pcm);
            }

            if (targetSamples <= 0)
                targetSamples = pcm.Count;

            return CreateOutputWithDecoderDelay(pcm, targetSamples);
        }

        private void DecodeSingleStream(byte[] data, int packetCount, List<short> pcm)
        {
            WmaProStreamDecoder stream = _streams[0];
            for (int packet = 0; packet < packetCount; packet++)
            {
                foreach (float[][] frame in stream.DecodePacket(data, packet * PacketSize, PacketSize))
                    AppendFrame(pcm, frame);
            }

            foreach (float[][] frame in stream.Flush())
                AppendFrame(pcm, frame);
        }

        private void DecodeInterleavedStreams(byte[] data, int packetCount, List<short> pcm)
        {
            for (int packet = 0; packet < packetCount; packet++)
            {
                WmaProStreamDecoder stream = _streams[_currentStream];
                foreach (float[][] frame in stream.DecodePacket(data, packet * PacketSize, PacketSize))
                    QueueFrame(_currentStream, frame);

                if (stream.PacketDone || stream.PacketLoss)
                    SelectNextStream();

                DrainQueuedSamples(pcm, holdbackSamples: 4096);
            }

            for (int i = 0; i < _streams.Length; i++)
            {
                foreach (float[][] frame in _streams[i].Flush())
                    QueueFrame(i, frame);
            }

            DrainQueuedSamples(pcm, holdbackSamples: 0);
        }

        private short[] CreateOutputWithDecoderDelay(List<short> pcm, int targetSamples)
        {
            short[] output = new short[targetSamples];
            int availableSamples = Math.Min(pcm.Count, targetSamples);
            int delaySamples = Math.Min(64 * _format.Channels, availableSamples);
            int copySamples = Math.Min(targetSamples, availableSamples - delaySamples);
            if (copySamples > 0)
                pcm.CopyTo(delaySamples, output, 0, copySamples);

            return output;
        }

        private void AppendFrame(List<short> output, float[][] frame)
        {
            int channels = _format.Channels;
            int samples = frame[0].Length;
            for (int i = 0; i < samples; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                    output.Add(FloatToPcm16(frame[ch][i]));
            }
        }

        private void QueueFrame(int streamIndex, float[][] frame)
        {
            for (int c = 0; c < _streams[streamIndex].Channels; c++)
                _queuedSamples[c, streamIndex].Enqueue(frame[c]);
        }

        private void DrainQueuedSamples(List<short> output, int holdbackSamples)
        {
            while (true)
            {
                int available = int.MaxValue;
                for (int s = 0; s < _streams.Length; s++)
                    available = Math.Min(available, _queuedSamples[0, s].Count);

                available -= Math.Min(available, holdbackSamples);
                if (available <= 0)
                    return;

                for (int i = 0; i < available; i++)
                {
                    for (int ch = 0; ch < _format.Channels; ch++)
                    {
                        int stream = _channelStream[ch];
                        int local = _channelLocal[ch];
                        output.Add(FloatToPcm16(_queuedSamples[local, stream].Dequeue()));
                    }
                }
            }
        }

        private void SelectNextStream()
        {
            if (_streams[_currentStream].SkipPackets != 0)
            {
                int minStream = 0;
                int minSkip = _streams[0].SkipPackets;
                for (int i = 1; i < _streams.Length; i++)
                {
                    if (_streams[i].SkipPackets < minSkip)
                    {
                        minSkip = _streams[i].SkipPackets;
                        minStream = i;
                    }
                }

                _currentStream = minStream;
            }

            foreach (WmaProStreamDecoder stream in _streams)
                stream.SkipPackets = Math.Max(0, stream.SkipPackets - 1);
        }
    }

    private sealed class WmaProStreamDecoder
    {
        private const uint DecodeFlags = 0x10d6;
        private const int BitsPerSample = 16;
        private const int BlockAlign = PacketSize;
        private const int MaxSubframes = 32;
        private const int MaxBands = 29;
        private const int BlockMinBits = 6;

        private readonly int _sampleRate;
        private readonly int _log2FrameSize;
        private readonly bool _lenPrefix;
        private readonly bool _dynamicRangeCompression;
        private readonly int _maxNumSubframes;
        private readonly int _subframeLenBits;
        private readonly bool _maxSubframeLenBit;
        private readonly int _minSamplesPerSubframe;
        private readonly int[] _numSfb = new int[8];
        private readonly int[][] _sfbOffsets = NewIntMatrix(8, MaxBands);
        private readonly int[][][] _sfOffsets = NewIntCube(8, 8, MaxBands);
        private readonly int[] _subwooferCutoffs = new int[8];
        private readonly ChannelContext[] _channel;
        private readonly ChannelGroup[] _chgroup;
        private readonly BitReservoir _reservoir = new();
        private readonly float[] _tmp = new float[SamplesPerFrame];

        private BitReader _gb = BitReader.Empty;
        private int _bufBitSize;
        private int _packetOffset;
        private int _numSavedBits;
        private bool _skipFrame = true;
        private bool _eofDone;
        private bool _parsedAllSubframes;
        private int _subframeLen;
        private int _channelsForCurSubframe;
        private readonly int[] _channelIndexesForCurSubframe = new int[MaxChannelsPerStream];
        private int _numBands;
        private bool _transmitNumVecCoeffs;
        private int[] _curSfbOffsets = [];
        private int _tableIdx;
        private int _escLen;
        private int _numChgroups;

        public WmaProStreamDecoder(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            Channels = channels;

            _channel = new ChannelContext[channels];
            for (int i = 0; i < channels; i++)
                _channel[i] = new ChannelContext(SamplesPerFrame);

            _chgroup = new ChannelGroup[channels];
            for (int i = 0; i < channels; i++)
                _chgroup[i] = new ChannelGroup();

            _log2FrameSize = Log2(BlockAlign) + 4;
            _lenPrefix = (DecodeFlags & 0x40) != 0;
            _dynamicRangeCompression = (DecodeFlags & 0x80) != 0;

            int log2MaxNumSubframes = (int)((DecodeFlags & 0x38) >> 3);
            _maxNumSubframes = 1 << log2MaxNumSubframes;
            _maxSubframeLenBit = _maxNumSubframes is 16 or 4;
            _subframeLenBits = Log2(log2MaxNumSubframes) + 1;
            _minSamplesPerSubframe = SamplesPerFrame / _maxNumSubframes;

            for (int i = 0; i < channels; i++)
                _channel[i].PrevBlockLen = SamplesPerFrame;

            InitScaleFactorBands(log2MaxNumSubframes + 1);
        }

        public int Channels { get; }

        public bool PacketDone { get; private set; }

        public bool PacketLoss { get; private set; } = true;

        public int SkipPackets { get; set; }

        public IEnumerable<float[][]> DecodePacket(byte[] data, int packetOffset, int packetSize)
        {
            int guard = 0;
            int consumed = 0;
            int packetBytes = Math.Min(packetSize, data.Length - packetOffset);
            while (guard++ < 64 && consumed < packetBytes)
            {
                PacketStepResult step = DecodePacketStep(data, packetOffset + consumed, packetBytes - consumed);
                consumed += step.BytesConsumed;

                float[][]? frame = step.Frame;
                if (frame != null)
                    yield return frame;

                if (PacketDone || PacketLoss)
                    yield break;
            }

            PacketLoss = true;
        }

        public IEnumerable<float[][]> Flush()
        {
            if (_eofDone)
                yield break;

            float[][] output = NewFloatMatrix(Channels, SamplesPerFrame);
            for (int c = 0; c < Channels; c++)
                Array.Copy(_channel[c].Out, 0, output[c], 0, SamplesPerFrame / 2);

            _eofDone = true;
            PacketDone = true;
            yield return output;
        }

        private PacketStepResult DecodePacketStep(byte[] packet, int packetStart, int availableBytes)
        {
            BitReader pgb;
            float[][]? output = null;

            if (packet.Length == 0 || packetStart >= packet.Length || availableBytes <= 0)
                return new PacketStepResult(null, 0);

            if (PacketDone || PacketLoss)
            {
                PacketDone = false;
                int bufSize = Math.Min(availableBytes, BlockAlign);
                _bufBitSize = bufSize << 3;
                pgb = new BitReader(packet, packetStart, _bufBitSize);

                _ = pgb.ReadBits(6);
                int numBitsPrevFrame = pgb.ReadBits(_log2FrameSize);
                pgb.SkipBits(3);
                SkipPackets = pgb.ReadBits(8);

                if (numBitsPrevFrame > 0)
                {
                    int remainingPacketBits = RemainingBits(pgb);
                    if (numBitsPrevFrame >= remainingPacketBits)
                    {
                        numBitsPrevFrame = remainingPacketBits;
                        PacketDone = true;
                    }

                    SaveBits(pgb, numBitsPrevFrame, append: true);
                    if (!PacketLoss)
                        output = DecodeFrame();
                }
                else if (_numSavedBits > 0)
                {
                    _numSavedBits = 0;
                    _reservoir.Clear();
                }

                if (PacketLoss)
                {
                    _numSavedBits = 0;
                    _reservoir.Clear();
                    PacketLoss = false;
                }
            }
            else
            {
                _bufBitSize = availableBytes << 3;
                pgb = new BitReader(packet, packetStart, _bufBitSize);
                pgb.SkipBits(_packetOffset);

                if (_lenPrefix &&
                    RemainingBits(pgb) > _log2FrameSize &&
                    pgb.ShowBits(_log2FrameSize) is int frameSize and > 0 &&
                    frameSize <= RemainingBits(pgb))
                {
                    SaveBits(pgb, frameSize, append: false);
                    if (!PacketLoss)
                    {
                        output = DecodeFrame();
                        PacketDone = !_lastFrameHadMoreFrames;
                    }
                }
                else
                {
                    PacketDone = true;
                }
            }

            if (RemainingBits(pgb) < 0)
                PacketLoss = true;

            if (PacketDone && !PacketLoss && RemainingBits(pgb) > 0)
                SaveBits(pgb, RemainingBits(pgb), append: false);

            _packetOffset = pgb.Position & 7;
            return new PacketStepResult(PacketLoss ? null : output, Math.Max(1, pgb.Position >> 3));
        }

        private bool _lastFrameHadMoreFrames;

        private readonly record struct PacketStepResult(float[][]? Frame, int BytesConsumed);

        private float[][]? DecodeFrame()
        {
            BitReader gb = _gb;
            int len = 0;

            if (_lenPrefix)
                len = gb.ReadBits(_log2FrameSize);

            if (!DecodeTileHeader(gb))
            {
                PacketLoss = true;
                return null;
            }

            if (Channels > 1 && gb.ReadBit())
            {
                if (gb.ReadBit())
                {
                    for (int i = 0; i < Channels * Channels; i++)
                        gb.SkipBits(4);
                }
            }

            if (_dynamicRangeCompression)
                gb.SkipBits(8);

            if (gb.ReadBit())
            {
                if (gb.ReadBit())
                    gb.SkipBits(Log2(SamplesPerFrame * 2));
                if (gb.ReadBit())
                    gb.SkipBits(Log2(SamplesPerFrame * 2));
            }

            _parsedAllSubframes = false;
            for (int i = 0; i < Channels; i++)
            {
                _channel[i].DecodedSamples = 0;
                _channel[i].CurSubframe = 0;
                _channel[i].ReuseScaleFactors = false;
            }

            while (!_parsedAllSubframes)
            {
                if (!DecodeSubframe(gb))
                {
                    PacketLoss = true;
                    return null;
                }
            }

            float[][]? output = null;
            if (!_skipFrame)
            {
                output = NewFloatMatrix(Channels, SamplesPerFrame);
                for (int i = 0; i < Channels; i++)
                    Array.Copy(_channel[i].Out, 0, output[i], 0, SamplesPerFrame);
            }
            else
            {
                _skipFrame = false;
            }

            for (int i = 0; i < Channels; i++)
                Array.Copy(_channel[i].Out, SamplesPerFrame, _channel[i].Out, 0, SamplesPerFrame / 2);

            if (_lenPrefix)
            {
                int used = gb.Position;
                int skip = len - used - 1;
                if (skip < 0)
                {
                    PacketLoss = true;
                    return output;
                }

                gb.SkipBits(skip);
            }
            else
            {
                while (gb.Position < _numSavedBits && !gb.ReadBit())
                {
                }
            }

            _lastFrameHadMoreFrames = gb.ReadBit();
            _gb = gb;
            return output;
        }

        private bool DecodeTileHeader(BitReader gb)
        {
            Span<int> numSamples = stackalloc int[MaxChannelsPerStream];
            Span<bool> containsSubframe = stackalloc bool[MaxChannelsPerStream];
            int channelsForCurSubframe = Channels;
            bool fixedChannelLayout = false;
            int minChannelLen = 0;

            for (int c = 0; c < Channels; c++)
                _channel[c].NumSubframes = 0;

            if (_maxNumSubframes == 1 || gb.ReadBit())
                fixedChannelLayout = true;

            do
            {
                for (int c = 0; c < Channels; c++)
                {
                    if (numSamples[c] == minChannelLen)
                    {
                        containsSubframe[c] = fixedChannelLayout ||
                            channelsForCurSubframe == 1 ||
                            minChannelLen == SamplesPerFrame - _minSamplesPerSubframe ||
                            gb.ReadBit();
                    }
                    else
                    {
                        containsSubframe[c] = false;
                    }
                }

                int subframeLen = DecodeSubframeLength(gb, minChannelLen);
                if (subframeLen <= 0)
                    return false;

                minChannelLen += subframeLen;
                for (int c = 0; c < Channels; c++)
                {
                    ChannelContext channel = _channel[c];
                    if (containsSubframe[c])
                    {
                        if (channel.NumSubframes >= MaxSubframes)
                            return false;

                        channel.SubframeLen[channel.NumSubframes] = subframeLen;
                        numSamples[c] += subframeLen;
                        channel.NumSubframes++;
                        if (numSamples[c] > SamplesPerFrame)
                            return false;
                    }
                    else if (numSamples[c] <= minChannelLen)
                    {
                        if (numSamples[c] < minChannelLen)
                        {
                            channelsForCurSubframe = 0;
                            minChannelLen = numSamples[c];
                        }

                        channelsForCurSubframe++;
                    }
                }
            }
            while (minChannelLen < SamplesPerFrame);

            for (int c = 0; c < Channels; c++)
            {
                int offset = 0;
                for (int i = 0; i < _channel[c].NumSubframes; i++)
                {
                    _channel[c].SubframeOffset[i] = offset;
                    offset += _channel[c].SubframeLen[i];
                }
            }

            _gb = gb;
            return true;
        }

        private int DecodeSubframeLength(BitReader gb, int offset)
        {
            if (offset == SamplesPerFrame - _minSamplesPerSubframe)
                return _minSamplesPerSubframe;

            int frameLenShift;
            if (_maxSubframeLenBit)
                frameLenShift = gb.ReadBit() ? 1 + gb.ReadBits(_subframeLenBits - 1) : 0;
            else
                frameLenShift = gb.ReadBits(_subframeLenBits);

            int subframeLen = SamplesPerFrame >> frameLenShift;
            return subframeLen < _minSamplesPerSubframe || subframeLen > SamplesPerFrame ? -1 : subframeLen;
        }

        private bool DecodeSubframe(BitReader gb)
        {
            int offset = SamplesPerFrame;
            int subframeLen = SamplesPerFrame;
            int totalSamples = SamplesPerFrame * Channels;
            bool transmitCoeffs = false;

            for (int i = 0; i < Channels; i++)
            {
                _channel[i].Grouped = false;
                if (offset > _channel[i].DecodedSamples)
                {
                    offset = _channel[i].DecodedSamples;
                    subframeLen = _channel[i].SubframeLen[_channel[i].CurSubframe];
                }
            }

            _channelsForCurSubframe = 0;
            for (int i = 0; i < Channels; i++)
            {
                int curSubframe = _channel[i].CurSubframe;
                totalSamples -= _channel[i].DecodedSamples;

                if (offset == _channel[i].DecodedSamples && subframeLen == _channel[i].SubframeLen[curSubframe])
                {
                    totalSamples -= _channel[i].SubframeLen[curSubframe];
                    _channel[i].DecodedSamples += _channel[i].SubframeLen[curSubframe];
                    _channelIndexesForCurSubframe[_channelsForCurSubframe++] = i;
                }
            }

            if (totalSamples == 0)
                _parsedAllSubframes = true;

            _tableIdx = Log2(SamplesPerFrame / subframeLen);
            _numBands = _numSfb[_tableIdx];
            _curSfbOffsets = _sfbOffsets[_tableIdx];
            int curSubwooferCutoff = _subwooferCutoffs[_tableIdx];

            int coeffOffset = offset + (SamplesPerFrame >> 1);
            for (int i = 0; i < _channelsForCurSubframe; i++)
                _channel[_channelIndexesForCurSubframe[i]].CoeffsOffset = coeffOffset;

            _subframeLen = subframeLen;
            _escLen = Log2(_subframeLen - 1) + 1;

            if (gb.ReadBit())
            {
                int numFillBits = gb.ReadBits(2);
                if (numFillBits == 0)
                {
                    int fillLen = gb.ReadBits(4);
                    numFillBits = gb.ReadBits(fillLen) + 1;
                }

                if (gb.Position + numFillBits > _numSavedBits)
                    return false;
                gb.SkipBits(numFillBits);
            }

            if (gb.ReadBit())
                return false;

            if (!DecodeChannelTransform(gb))
                return false;

            for (int i = 0; i < _channelsForCurSubframe; i++)
            {
                int c = _channelIndexesForCurSubframe[i];
                _channel[c].TransmitCoefs = gb.ReadBit();
                transmitCoeffs |= _channel[c].TransmitCoefs;
            }

            if (transmitCoeffs)
            {
                int quantStep = 90 * BitsPerSample >> 4;

                _transmitNumVecCoeffs = gb.ReadBit();
                if (_transmitNumVecCoeffs)
                {
                    int numBits = Log2((_subframeLen + 3) / 4) + 1;
                    for (int i = 0; i < _channelsForCurSubframe; i++)
                    {
                        int c = _channelIndexesForCurSubframe[i];
                        int numVecCoeffs = gb.ReadBits(numBits) << 2;
                        if (numVecCoeffs > _subframeLen)
                            return false;
                        _channel[c].NumVecCoeffs = numVecCoeffs;
                    }
                }
                else
                {
                    for (int i = 0; i < _channelsForCurSubframe; i++)
                        _channel[_channelIndexesForCurSubframe[i]].NumVecCoeffs = _subframeLen;
                }

                int step = gb.ReadSignedBits(6);
                quantStep += step;
                if (step is -32 or 31)
                {
                    int sign = step == 31 ? 0 : -1;
                    int quant = 0;
                    while (gb.Position + 5 < _numSavedBits && (step = gb.ReadBits(5)) == 31)
                        quant += 31;
                    quantStep += ((quant + step) ^ sign) - sign;
                }

                if (_channelsForCurSubframe == 1)
                {
                    _channel[_channelIndexesForCurSubframe[0]].QuantStep = quantStep;
                }
                else
                {
                    int modifierLen = gb.ReadBits(3);
                    for (int i = 0; i < _channelsForCurSubframe; i++)
                    {
                        int c = _channelIndexesForCurSubframe[i];
                        _channel[c].QuantStep = quantStep;
                        if (gb.ReadBit())
                            _channel[c].QuantStep += modifierLen != 0 ? gb.ReadBits(modifierLen) + 1 : 1;
                    }
                }

                if (!DecodeScaleFactors(gb))
                    return false;
            }

            for (int i = 0; i < _channelsForCurSubframe; i++)
            {
                int c = _channelIndexesForCurSubframe[i];
                if (_channel[c].TransmitCoefs && gb.Position < _numSavedBits)
                {
                    if (!DecodeCoefficients(gb, c))
                        return false;
                }
                else
                {
                    Array.Clear(_channel[c].Out, _channel[c].CoeffsOffset, subframeLen);
                }
            }

            if (transmitCoeffs)
            {
                InverseChannelTransform();
                for (int i = 0; i < _channelsForCurSubframe; i++)
                {
                    int c = _channelIndexesForCurSubframe[i];
                    ChannelContext channel = _channel[c];

                    for (int b = 0; b < _numBands; b++)
                    {
                        int end = Math.Min(_curSfbOffsets[b + 1], _subframeLen);
                        int exp = channel.QuantStep -
                                  (channel.MaxScaleFactor - channel.ScaleFactors[b]) *
                                  channel.ScaleFactorStep;
                        float quant = QuantScale.Get(exp);
                        int start = _curSfbOffsets[b];

                        for (int x = start; x < end; x++)
                            _tmp[x] = channel.Out[channel.CoeffsOffset + x] * quant;
                    }

                    Imdct.Transform(_tmp, channel.Out, channel.CoeffsOffset, subframeLen);
                }
            }

            ApplyWindow();

            for (int i = 0; i < _channelsForCurSubframe; i++)
                {
                    int c = _channelIndexesForCurSubframe[i];
                    if (_channel[c].CurSubframe >= _channel[c].NumSubframes)
                        return false;
                    _channel[c].CurSubframe++;
                }

            _gb = gb;
            return true;
        }

        private bool DecodeChannelTransform(BitReader gb)
        {
            _numChgroups = 0;
            if (Channels <= 1)
                return true;

            int remainingChannels = _channelsForCurSubframe;
            if (gb.ReadBit())
                return false;

            while (remainingChannels != 0 && _numChgroups < _channelsForCurSubframe)
            {
                ChannelGroup group = _chgroup[_numChgroups];
                group.NumChannels = 0;
                group.Transform = false;
                Array.Clear(group.ChannelIndexes);
                Array.Clear(group.TransformBand);
                Array.Clear(group.DecorrelationMatrix);

                if (remainingChannels > 2)
                {
                    for (int i = 0; i < _channelsForCurSubframe; i++)
                    {
                        int channelIdx = _channelIndexesForCurSubframe[i];
                        if (!_channel[channelIdx].Grouped && gb.ReadBit())
                        {
                            group.ChannelIndexes[group.NumChannels++] = channelIdx;
                            _channel[channelIdx].Grouped = true;
                        }
                    }
                }
                else
                {
                    group.NumChannels = remainingChannels;
                    int outIndex = 0;
                    for (int i = 0; i < _channelsForCurSubframe; i++)
                    {
                        int channelIdx = _channelIndexesForCurSubframe[i];
                        if (!_channel[channelIdx].Grouped)
                            group.ChannelIndexes[outIndex++] = channelIdx;
                        _channel[channelIdx].Grouped = true;
                    }
                }

                if (group.NumChannels == 2)
                {
                    if (gb.ReadBit())
                    {
                        if (gb.ReadBit())
                            return false;
                    }
                    else
                    {
                        group.Transform = true;
                        group.DecorrelationMatrix[0] = 1.0f;
                        group.DecorrelationMatrix[1] = -1.0f;
                        group.DecorrelationMatrix[2] = 1.0f;
                        group.DecorrelationMatrix[3] = 1.0f;
                    }
                }
                else if (group.NumChannels > 2 && gb.ReadBit())
                {
                    return false;
                }

                if (group.Transform)
                {
                    if (!gb.ReadBit())
                    {
                        for (int i = 0; i < _numBands; i++)
                            group.TransformBand[i] = gb.ReadBit();
                    }
                    else
                    {
                        Array.Fill(group.TransformBand, true, 0, _numBands);
                    }
                }

                remainingChannels -= group.NumChannels;
                _numChgroups++;
            }

            return true;
        }

        private bool DecodeScaleFactors(BitReader gb)
        {
            for (int i = 0; i < _channelsForCurSubframe; i++)
            {
                int c = _channelIndexesForCurSubframe[i];
                ChannelContext channel = _channel[c];
                channel.ScaleFactors = channel.SavedScaleFactors[1 - channel.ScaleFactorIdx];

                if (channel.ReuseScaleFactors)
                {
                    int[] sfOffsets = _sfOffsets[_tableIdx][channel.TableIdx];
                    for (int b = 0; b < _numBands; b++)
                        channel.ScaleFactors[b] = channel.SavedScaleFactors[channel.ScaleFactorIdx][sfOffsets[b]];
                }

                if (channel.CurSubframe == 0 || gb.ReadBit())
                {
                    if (!channel.ReuseScaleFactors)
                    {
                        channel.ScaleFactorStep = gb.ReadBits(2) + 1;
                        int val = 45 / channel.ScaleFactorStep;
                        for (int b = 0; b < _numBands; b++)
                        {
                            val += Xma2Tables.Scale.Decode(gb);
                            channel.ScaleFactors[b] = val;
                        }
                    }
                    else
                    {
                        for (int b = 0; b < _numBands; b++)
                        {
                            int idx = Xma2Tables.ScaleRunLevel.Decode(gb);
                            int skip;
                            int val;
                            int sign;

                            if (idx == 0)
                            {
                                int code = gb.ReadBits(14);
                                val = code >> 6;
                                sign = (code & 1) - 1;
                                skip = (code & 0x3f) >> 1;
                            }
                            else if (idx == 1)
                            {
                                break;
                            }
                            else
                            {
                                skip = Xma2Tables.ScaleRun[idx];
                                val = Xma2Tables.ScaleLevel[idx];
                                sign = gb.ReadBit() ? 0 : -1;
                            }

                            b += skip;
                            if (b >= _numBands)
                                return false;

                            channel.ScaleFactors[b] += (val ^ sign) - sign;
                        }
                    }

                    channel.ScaleFactorIdx = 1 - channel.ScaleFactorIdx;
                    channel.TableIdx = _tableIdx;
                    channel.ReuseScaleFactors = true;
                }

                channel.MaxScaleFactor = channel.ScaleFactors[0];
                for (int b = 1; b < _numBands; b++)
                    channel.MaxScaleFactor = Math.Max(channel.MaxScaleFactor, channel.ScaleFactors[b]);
            }

            return true;
        }

        private bool DecodeCoefficients(BitReader gb, int c)
        {
            ChannelContext channel = _channel[c];
            bool rlMode = false;
            int curCoeff = 0;
            int numZeros = 0;
            Span<float> vals = stackalloc float[4];

            bool tableIndex = gb.ReadBit();
            VlcDecoder coefVlc = tableIndex ? Xma2Tables.Coef1 : Xma2Tables.Coef0;
            ushort[] run = tableIndex ? Xma2Tables.Coef1Run : Xma2Tables.Coef0Run;
            float[] level = tableIndex ? Xma2Tables.Coef1Level : Xma2Tables.Coef0Level;

            while ((_transmitNumVecCoeffs || !rlMode) && curCoeff + 3 < channel.NumVecCoeffs)
            {
                int idx = Xma2Tables.Vector4.Decode(gb);

                if (idx < 0)
                {
                    for (int i = 0; i < 4; i += 2)
                    {
                        idx = Xma2Tables.Vector2.Decode(gb);
                        if (idx < 0)
                        {
                            int v0 = Xma2Tables.Vector1.Decode(gb);
                            if (v0 == Xma2Tables.HuffVec1Size - 1)
                                v0 += (int)GetLargeValue(gb);
                            int v1 = Xma2Tables.Vector1.Decode(gb);
                            if (v1 == Xma2Tables.HuffVec1Size - 1)
                                v1 += (int)GetLargeValue(gb);

                            vals[i] = v0;
                            vals[i + 1] = v1;
                        }
                        else
                        {
                            vals[i] = idx >> 4;
                            vals[i + 1] = idx & 0x0f;
                        }
                    }
                }
                else
                {
                    vals[0] = idx >> 12;
                    vals[1] = (idx >> 8) & 0x0f;
                    vals[2] = (idx >> 4) & 0x0f;
                    vals[3] = idx & 0x0f;
                }

                for (int i = 0; i < 4; i++)
                {
                    if (vals[i] != 0)
                    {
                        channel.Out[channel.CoeffsOffset + curCoeff] = gb.ReadBit() ? vals[i] : -vals[i];
                        numZeros = 0;
                    }
                    else
                    {
                        channel.Out[channel.CoeffsOffset + curCoeff] = 0;
                        rlMode |= ++numZeros > (_subframeLen >> 8);
                    }

                    curCoeff++;
                }
            }

            if (curCoeff < _subframeLen)
            {
                Array.Clear(channel.Out, channel.CoeffsOffset + curCoeff, _subframeLen - curCoeff);
                return RunLevelDecode(gb, coefVlc, level, run, channel.Out, channel.CoeffsOffset, curCoeff, _subframeLen, _subframeLen, _escLen);
            }

            return true;
        }

        private static bool RunLevelDecode(BitReader gb, VlcDecoder vlc, float[] levelTable, ushort[] runTable, float[] output, int outputOffset, int offset, int numCoefs, int blockLen, int frameLenBits)
        {
            int coefMask = blockLen - 1;
            while (offset < numCoefs)
            {
                int code = vlc.Decode(gb);
                bool endOfBlock = false;
                if (code > 1)
                {
                    offset += runTable[code];
                    output[outputOffset + (offset & coefMask)] = gb.ReadBit() ? levelTable[code] : -levelTable[code];
                }
                else if (code == 1)
                {
                    endOfBlock = true;
                }
                else
                {
                    int level = (int)GetLargeValue(gb);
                    if (gb.ReadBit())
                    {
                        if (gb.ReadBit())
                        {
                            if (gb.ReadBit())
                                return false;
                            offset += gb.ReadBits(frameLenBits) + 4;
                        }
                        else
                        {
                            offset += gb.ReadBits(2) + 1;
                        }
                    }

                    output[outputOffset + (offset & coefMask)] = gb.ReadBit() ? level : -level;
                }

                if (endOfBlock)
                    break;

                offset++;
            }

            return offset <= numCoefs;
        }

        private void InverseChannelTransform()
        {
            Span<float> data = stackalloc float[MaxChannelsPerStream];

            for (int i = 0; i < _numChgroups; i++)
            {
                ChannelGroup group = _chgroup[i];
                if (!group.Transform)
                    continue;

                for (int b = 0; b < _numBands; b++)
                {
                    int start = _curSfbOffsets[b];
                    int end = Math.Min(_curSfbOffsets[b + 1], _subframeLen);

                    if (group.TransformBand[b])
                    {
                        for (int y = start; y < end; y++)
                        {
                            for (int ch = 0; ch < group.NumChannels; ch++)
                            {
                                ChannelContext channel = _channel[group.ChannelIndexes[ch]];
                                data[ch] = channel.Out[channel.CoeffsOffset + y];
                            }

                            for (int ch = 0; ch < group.NumChannels; ch++)
                            {
                                float sum = 0;
                                for (int x = 0; x < group.NumChannels; x++)
                                    sum += data[x] * group.DecorrelationMatrix[(ch * group.NumChannels) + x];

                                ChannelContext channel = _channel[group.ChannelIndexes[ch]];
                                channel.Out[channel.CoeffsOffset + y] = sum;
                            }
                        }
                    }
                    else if (Channels == 2)
                    {
                        for (int ch = 0; ch < group.NumChannels; ch++)
                        {
                            ChannelContext channel = _channel[group.ChannelIndexes[ch]];
                            for (int y = start; y < end; y++)
                                channel.Out[channel.CoeffsOffset + y] *= 181.0f / 128.0f;
                        }
                    }
                }
            }
        }

        private void ApplyWindow()
        {
            for (int i = 0; i < _channelsForCurSubframe; i++)
            {
                int c = _channelIndexesForCurSubframe[i];
                ChannelContext channel = _channel[c];
                int winLen = channel.PrevBlockLen;
                int start = channel.CoeffsOffset - (winLen >> 1);

                if (_subframeLen < winLen)
                {
                    start += (winLen - _subframeLen) >> 1;
                    winLen = _subframeLen;
                }

                float[] window = SineWindow.Get(winLen);
                VectorFmulWindow(channel.Out, start, start, start + (winLen >> 1), window, winLen >> 1);
                channel.PrevBlockLen = _subframeLen;
            }
        }

        private static void VectorFmulWindow(float[] data, int dst, int src0, int src1, float[] window, int len)
        {
            int dstMid = dst + len;
            int src0Mid = src0 + len;
            int winMid = len;

            for (int i = -len, j = len - 1; i < 0; i++, j--)
            {
                float s0 = data[src0Mid + i];
                float s1 = data[src1 + j];
                float wi = window[winMid + i];
                float wj = window[winMid + j];
                data[dstMid + i] = (s0 * wj) - (s1 * wi);
                data[dstMid + j] = (s0 * wi) + (s1 * wj);
            }
        }

        private void SaveBits(BitReader source, int length, bool append)
        {
            if (!append)
                _reservoir.Clear();

            _reservoir.Append(source, length);
            _numSavedBits = _reservoir.BitCount;
            _gb = _reservoir.CreateReader();
        }

        private int RemainingBits(BitReader reader) => _bufBitSize - reader.Position;

        private void InitScaleFactorBands(int numPossibleBlockSizes)
        {
            for (int i = 0; i < numPossibleBlockSizes; i++)
            {
                int subframeLen = SamplesPerFrame >> i;
                int band = 1;
                int rate = GetCodecRate(_sampleRate);
                _sfbOffsets[i][0] = 0;

                for (int x = 0; x < MaxBands - 1 && _sfbOffsets[i][band - 1] < subframeLen; x++)
                {
                    int offset = (subframeLen * 2 * Xma2Tables.CriticalFrequencies[x]) / rate + 2;
                    offset &= ~3;
                    if (offset > _sfbOffsets[i][band - 1])
                        _sfbOffsets[i][band++] = offset;

                    if (offset >= subframeLen)
                        break;
                }

                _sfbOffsets[i][band - 1] = subframeLen;
                _numSfb[i] = band - 1;
            }

            for (int i = 0; i < numPossibleBlockSizes; i++)
            {
                for (int b = 0; b < _numSfb[i]; b++)
                {
                    int offset = ((_sfbOffsets[i][b] + _sfbOffsets[i][b + 1] - 1) << i) >> 1;
                    for (int x = 0; x < numPossibleBlockSizes; x++)
                    {
                        int v = 0;
                        while ((_sfbOffsets[x][v + 1] << x) < offset)
                            v++;
                        _sfOffsets[i][x][b] = v;
                    }
                }
            }

            for (int i = 0; i < numPossibleBlockSizes; i++)
            {
                int blockSize = SamplesPerFrame >> i;
                int cutoff = (440 * blockSize + (3 * (_sampleRate >> 1)) - 1) / _sampleRate;
                _subwooferCutoffs[i] = Math.Clamp(cutoff, 4, blockSize);
            }
        }

        private static int GetCodecRate(int sampleRate)
        {
            if (sampleRate > 44100)
                return 48000;
            if (sampleRate > 32000)
                return 44100;
            if (sampleRate > 24000)
                return 32000;
            return 24000;
        }
    }

    private sealed class ChannelContext
    {
        public ChannelContext(int samplesPerFrame)
        {
            Out = new float[samplesPerFrame + (samplesPerFrame / 2)];
            SavedScaleFactors = [new int[29], new int[29]];
            ScaleFactors = SavedScaleFactors[0];
        }

        public int PrevBlockLen;
        public bool TransmitCoefs;
        public int NumSubframes;
        public int[] SubframeLen { get; } = new int[32];
        public int[] SubframeOffset { get; } = new int[32];
        public int CurSubframe;
        public int DecodedSamples;
        public bool Grouped;
        public int QuantStep;
        public bool ReuseScaleFactors;
        public int ScaleFactorStep;
        public int MaxScaleFactor;
        public int[][] SavedScaleFactors { get; }
        public int ScaleFactorIdx;
        public int[] ScaleFactors;
        public int TableIdx;
        public float[] Out { get; }
        public int CoeffsOffset;
        public int NumVecCoeffs;
    }

    private sealed class ChannelGroup
    {
        public int NumChannels;
        public bool Transform;
        public bool[] TransformBand { get; } = new bool[29];
        public float[] DecorrelationMatrix { get; } = new float[MaxChannelsPerStream * MaxChannelsPerStream];
        public int[] ChannelIndexes { get; } = new int[MaxChannelsPerStream];
    }

    internal sealed class VlcDecoder
    {
        private readonly List<Node> _nodes = [new()];

        public VlcDecoder(ReadOnlySpan<byte> lengths, ReadOnlySpan<ushort> symbols, int offset)
        {
            ulong code = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                int len = lengths[i];
                if (len > 0)
                {
                    int symbol = symbols.Length > 0 ? symbols[i] + offset : i + offset;
                    AddCode((uint)(code >> (32 - len)), len, symbol);
                    code += 1UL << (32 - len);
                }
            }
        }

        public VlcDecoder(ReadOnlySpan<(ushort Symbol, byte Length)> table, int offset)
        {
            ulong code = 0;
            for (int i = 0; i < table.Length; i++)
            {
                int len = table[i].Length;
                if (len > 0)
                {
                    AddCode((uint)(code >> (32 - len)), len, table[i].Symbol + offset);
                    code += 1UL << (32 - len);
                }
            }
        }

        public int Decode(BitReader reader)
        {
            int nodeIndex = 0;
            while (true)
            {
                Node node = _nodes[nodeIndex];
                if (node.Symbol.HasValue)
                    return node.Symbol.Value;

                bool bit = reader.ReadBit();
                nodeIndex = bit ? node.One : node.Zero;
                if (nodeIndex <= 0)
                    throw new InvalidDataException($"Invalid XMA2 Huffman code at bit {reader.Position}.");
            }
        }

        private void AddCode(uint code, int length, int symbol)
        {
            int nodeIndex = 0;
            for (int bit = length - 1; bit >= 0; bit--)
            {
                bool one = ((code >> bit) & 1) != 0;
                Node node = _nodes[nodeIndex];
                int next = one ? node.One : node.Zero;
                if (next == 0)
                {
                    next = _nodes.Count;
                    _nodes.Add(new Node());
                    if (one)
                        node.One = next;
                    else
                        node.Zero = next;
                    _nodes[nodeIndex] = node;
                }

                nodeIndex = next;
            }

            Node leaf = _nodes[nodeIndex];
            leaf.Symbol = symbol;
            _nodes[nodeIndex] = leaf;
        }

        private struct Node
        {
            public int Zero;
            public int One;
            public int? Symbol;
        }
    }

    internal sealed class BitReader
    {
        public static BitReader Empty => new([], 0);

        private readonly byte[] _data;
        private readonly int _byteOffset;
        private readonly int _bitLength;

        public BitReader(byte[] data, int bitLength)
            : this(data, 0, bitLength)
        {
        }

        public BitReader(byte[] data, int byteOffset, int bitLength)
        {
            _data = data;
            _byteOffset = byteOffset;
            _bitLength = bitLength;
            Position = 0;
        }

        public int Position { get; private set; }

        public bool ReadBit()
        {
            if (Position >= _bitLength)
                return false;

            int byteIndex = Position >> 3;
            int bitIndex = 7 - (Position & 7);
            Position++;
            return ((_data[_byteOffset + byteIndex] >> bitIndex) & 1) != 0;
        }

        public int ReadBits(int count)
        {
            if (count <= 0)
                return 0;

            int value = 0;
            for (int i = 0; i < count; i++)
                value = (value << 1) | (ReadBit() ? 1 : 0);
            return value;
        }

        public int ReadSignedBits(int count)
        {
            int value = ReadBits(count);
            int signBit = 1 << (count - 1);
            return (value ^ signBit) - signBit;
        }

        public int ShowBits(int count)
        {
            int oldPosition = Position;
            int value = ReadBits(count);
            Position = oldPosition;
            return value;
        }

        public void SkipBits(int count)
        {
            Position = Math.Min(_bitLength, Position + Math.Max(0, count));
        }
    }

    private sealed class BitReservoir
    {
        private readonly List<byte> _data = [];

        public int BitCount { get; private set; }

        public void Clear()
        {
            _data.Clear();
            BitCount = 0;
        }

        public void Append(BitReader source, int bitCount)
        {
            for (int i = 0; i < bitCount; i++)
                AppendBit(source.ReadBit());
        }

        public BitReader CreateReader() => new([.. _data], BitCount);

        private void AppendBit(bool bit)
        {
            int bitOffset = BitCount & 7;
            if (bitOffset == 0)
                _data.Add(0);

            if (bit)
                _data[^1] |= (byte)(1 << (7 - bitOffset));

            BitCount++;
        }
    }

    private sealed class SampleQueue
    {
        private readonly Queue<float[]> _blocks = new();
        private float[]? _current;
        private int _index;

        public int Count { get; private set; }

        public void Enqueue(float[] samples)
        {
            _blocks.Enqueue(samples);
            Count += samples.Length;
        }

        public float Dequeue()
        {
            if (_current == null || _index >= _current.Length)
            {
                _current = _blocks.Dequeue();
                _index = 0;
            }

            Count--;
            return _current[_index++];
        }
    }

    private static class SineWindow
    {
        private static readonly Dictionary<int, float[]> Cache = [];

        public static float[] Get(int length)
        {
            if (Cache.TryGetValue(length, out float[]? window))
                return window;

            window = new float[length];
            for (int i = 0; i < length; i++)
                window[i] = (float)Math.Sin((i + 0.5) * (Math.PI / (2.0 * length)));

            Cache[length] = window;
            return window;
        }
    }

    private static class Imdct
    {
        private static readonly Dictionary<int, MatrixSet> Matrices = [];

        public static void Transform(float[] input, float[] output, int outputOffset, int length)
        {
            MatrixSet matrices = GetMatrices(length);
            int half = length >> 1;

            for (int i = 0; i < half; i++)
                output[outputOffset + i] = Dot(input, matrices.Down[i], length);
            for (int i = 0; i < half; i++)
                output[outputOffset + half + i] = Dot(input, matrices.Up[i], length);
        }

        private static MatrixSet GetMatrices(int length)
        {
            if (Matrices.TryGetValue(length, out MatrixSet? existing))
                return existing;

            int half = length >> 1;
            double phase = Math.PI / (4.0 * length);
            double scale = 1.0 / half / (1 << 15);

            float[][] down = NewFloatMatrix(half, length);
            float[][] up = NewFloatMatrix(half, length);
            for (int i = 0; i < half; i++)
            {
                double downPhase = phase * ((2 * length) - (2 * i) - 1);
                double upPhase = phase * ((3 * length) + (2 * i) + 1);
                for (int j = 0; j < length; j++)
                {
                    double a = (2 * j) + 1;
                    down[i][j] = (float)(Math.Cos(a * downPhase) * scale);
                    up[i][j] = (float)(-Math.Cos(a * upPhase) * scale);
                }
            }

            MatrixSet created = new(down, up);
            Matrices[length] = created;
            return created;
        }

        private static float Dot(float[] left, float[] right, int length)
        {
            int width = Vector<float>.Count;
            int i = 0;
            Vector<float> sum = Vector<float>.Zero;
            for (; i <= length - width; i += width)
                sum += new Vector<float>(left, i) * new Vector<float>(right, i);

            float value = Vector.Sum(sum);
            for (; i < length; i++)
                value += left[i] * right[i];

            return value;
        }

        private sealed record MatrixSet(float[][] Down, float[][] Up);
    }

    private static class QuantScale
    {
        private const int MinExponent = -512;
        private const int MaxExponent = 512;

        private static readonly float[] Values = CreateValues();

        public static float Get(int exponent)
        {
            return exponent is >= MinExponent and <= MaxExponent
                ? Values[exponent - MinExponent]
                : (float)Math.Pow(10.0, exponent / 20.0);
        }

        private static float[] CreateValues()
        {
            float[] values = new float[MaxExponent - MinExponent + 1];
            for (int exponent = MinExponent; exponent <= MaxExponent; exponent++)
                values[exponent - MinExponent] = (float)Math.Pow(10.0, exponent / 20.0);

            return values;
        }
    }

    private static uint GetLargeValue(BitReader reader)
    {
        int bits = 8;
        if (reader.ReadBit())
        {
            bits += 8;
            if (reader.ReadBit())
            {
                bits += 8;
                if (reader.ReadBit())
                    bits += 7;
            }
        }

        return (uint)reader.ReadBits(bits);
    }

    private static short FloatToPcm16(float value)
    {
        value = Math.Clamp(value, -1.0f, 1.0f);
        return (short)Math.Clamp((int)(value * 32767.0f), short.MinValue, short.MaxValue);
    }

    private static int Log2(int value)
    {
        if (value <= 0)
            return 0;
        return BitOperations.Log2((uint)value);
    }

    private static ushort ReadU16(BinaryReader reader, bool isBigEndian)
    {
        byte[] b = reader.ReadBytes(2);
        if (isBigEndian)
            Array.Reverse(b);
        return BitConverter.ToUInt16(b, 0);
    }

    private static uint ReadU32(BinaryReader reader, bool isBigEndian)
    {
        byte[] b = reader.ReadBytes(4);
        if (isBigEndian)
            Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static int[][] NewIntMatrix(int rows, int columns)
    {
        int[][] matrix = new int[rows][];
        for (int i = 0; i < rows; i++)
            matrix[i] = new int[columns];
        return matrix;
    }

    private static int[][][] NewIntCube(int x, int y, int z)
    {
        int[][][] cube = new int[x][][];
        for (int i = 0; i < x; i++)
            cube[i] = NewIntMatrix(y, z);
        return cube;
    }

    private static float[][] NewFloatMatrix(int rows, int columns)
    {
        float[][] matrix = new float[rows][];
        for (int i = 0; i < rows; i++)
            matrix[i] = new float[columns];
        return matrix;
    }
}
