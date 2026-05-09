using System.Numerics;

namespace JustDanceEditor.Audio.Codecs;

internal static class Xma2Encoder
{
    private const int PacketSize = 2048;
    private const int SamplesPerFrame = 512;
    private const int MinSubframeSamples = 128;
    private const int DecoderPrerollSamples = SamplesPerFrame + 64;
    private const int MaxBands = 29;
    private const int MaxQuantizedCoefficient = 1024;
    private const double TargetCoefficientMagnitude = 35.0;
    private const int BaseQuantBoost = 0;
    private const float UntransformedBandScale = 128.0f / 181.0f;
    private static readonly BandTarget OriginalBandTarget = new(0.6, 0.25);

    private static readonly VlcCodebook ScaleCodes = VlcCodebook.FromTable(Xma2Tables.ScaleTable, -60);
    private static readonly VlcCodebook ScaleRunLevelCodes = VlcCodebook.FromTable(Xma2Tables.ScaleRunLevelTable, 0);
    private static readonly VlcCodebook Vector4Codes = VlcCodebook.FromLengths(Xma2Tables.Vector4Lengths, Xma2Tables.Vector4Symbols, -1);
    private static readonly VlcCodebook Vector2Codes = VlcCodebook.FromTable(Xma2Tables.Vector2Table, -1);
    private static readonly VlcCodebook Vector1Codes = VlcCodebook.FromTable(Xma2Tables.Vector1Table, 0);

    public static byte[] Encode(short[] interleavedPcm, int sampleRate, int channels, out int samplesEncoded)
    {
        if (sampleRate != 48000)
            throw new NotSupportedException("The managed XMA2 encoder currently expects 48000 Hz PCM.");
        if (channels is < 1 or > 2)
            throw new NotSupportedException("The managed XMA2 encoder currently supports mono or stereo streams.");
        if (interleavedPcm.Length % channels != 0)
            throw new InvalidDataException("PCM sample count is not aligned to the channel count.");

        samplesEncoded = interleavedPcm.Length / channels;
        int frameCount = Math.Max(1, (samplesEncoded + DecoderPrerollSamples + SamplesPerFrame - 1) / SamplesPerFrame);
        int paddedSamples = frameCount * SamplesPerFrame;

        float[][] target = BuildDelayedTarget(interleavedPcm, channels, samplesEncoded, paddedSamples);
        ScaleBandLayout bands = ScaleBandLayout.Create(sampleRate);
        FrameAnalysisState analysisState = new(channels);

        List<EncodedFrame> frames = new(frameCount);
        for (int frame = 0; frame < frameCount; frame++)
            frames.Add(EncodeFrame(target, channels, frame, bands, analysisState));

        return Packetize(frames);
    }

    private static float[][] BuildDelayedTarget(short[] interleavedPcm, int channels, int samplesEncoded, int paddedSamples)
    {
        float[][] target = NewFloatMatrix(channels, paddedSamples);
        for (int sample = 0; sample < samplesEncoded; sample++)
        {
            int delayed = sample + DecoderPrerollSamples;
            if (delayed >= paddedSamples)
                break;

            for (int ch = 0; ch < channels; ch++)
                target[ch][delayed] = interleavedPcm[(sample * channels) + ch] / 32767.0f;
        }

        return target;
    }

    private static EncodedFrame EncodeFrame(float[][] target, int channels, int frameIndex, ScaleBandLayout bands, FrameAnalysisState analysisState)
        => EncodeFullFrame(target, channels, frameIndex, bands, analysisState);

    private static EncodedFrame EncodeFullFrame(float[][] target, int channels, int frameIndex, ScaleBandLayout bands, FrameAnalysisState analysisState)
    {
        int quantBoost = 0;
        double frameRms = GetFrameRms(target, channels, frameIndex);
        double baseCoefficientTarget = GetFrameCoefficientTarget(target, channels, frameIndex);

        while (true)
        {
            FrameCandidate? best = null;
            foreach (double coefficientTarget in GetCandidateCoefficientTargets(baseCoefficientTarget, frameRms))
            {
                foreach (BandTarget bandTarget in GetCandidateBandTargets(frameRms))
                {
                    foreach (bool? stereoMode in GetCandidateStereoModes(channels, frameRms))
                    {
                        FrameCandidate candidate = BuildFrameCandidate(
                            target,
                            channels,
                            frameIndex,
                            bands,
                            quantBoost,
                            coefficientTarget,
                            bandTarget,
                            stereoMode,
                            analysisState);

                        if (candidate.TotalBits <= ((PacketSize * 8) - 32) && IsBetterCandidate(candidate, best))
                            best = candidate;
                    }
                }
            }

            if (best is not null)
            {
                analysisState.Commit(best.ImdctOutput);
                return new EncodedFrame(best.Body, best.BodyBits, best.TotalBits);
            }

            quantBoost += 3;
            if (quantBoost > 48)
                throw new InvalidDataException("Unable to fit encoded XMA2 frame into one packet.");
        }
    }

    private static FrameCandidate BuildFrameCandidate(
        float[][] target,
        int channels,
        int frameIndex,
        ScaleBandLayout bands,
        int quantBoost,
        double coefficientTarget,
        BandTarget bandTarget,
        bool? stereoMode,
        FrameAnalysisState analysisState)
    {
        ChannelEncoding[] encodedChannels = new ChannelEncoding[channels];
        float[][] desiredImdctOutput = NewFloatMatrix(channels, SamplesPerFrame);
        bool[]? stereoTransformBands = null;

        if (channels == 2)
        {
            desiredImdctOutput[0] = BuildImdctOutput(target[0], frameIndex);
            desiredImdctOutput[1] = BuildImdctOutput(target[1], frameIndex);
            float[] left = Mdct.Forward(desiredImdctOutput[0]);
            float[] right = Mdct.Forward(desiredImdctOutput[1]);
            stereoTransformBands = stereoMode switch
            {
                true => CreateAllTransformBands(bands.Count),
                false => null,
                _ => ChooseStereoTransformBands(left, right, bands, target, frameIndex * SamplesPerFrame),
            };

            if (stereoTransformBands is not null)
            {
                ApplyStereoTransform(left, right, bands, stereoTransformBands);
                QuantizeStereoPair(left, right, bands, quantBoost, coefficientTarget, bandTarget, encodedChannels);
            }
            else
            {
                encodedChannels[0] = QuantizeChannel(left, bands, quantBoost, coefficientTarget, bandTarget);
                encodedChannels[1] = QuantizeChannel(right, bands, quantBoost, coefficientTarget, bandTarget);
            }
        }
        else
        {
            for (int ch = 0; ch < channels; ch++)
            {
                desiredImdctOutput[ch] = BuildImdctOutput(target[ch], frameIndex);
                float[] spectrum = Mdct.Forward(desiredImdctOutput[ch]);
                encodedChannels[ch] = QuantizeChannel(spectrum, bands, quantBoost, coefficientTarget, bandTarget);
            }
        }

        float[][] imdctOutput = ReconstructImdctOutput(encodedChannels, channels, bands, stereoTransformBands);
        BitWriter body = new();
        WriteFrameBody(body, [new SubframeEncoding(encodedChannels, stereoTransformBands, bands)], channels);
        int totalBits = 15 + body.BitCount + 1;
        FrameError frameError = analysisState.MeasureError(imdctOutput, target, frameIndex);
        double error = frameError.Error + CalculateFutureOverlapError(imdctOutput, desiredImdctOutput, SamplesPerFrame);
        bool risky = IsRiskyCandidate(frameError);
        return new FrameCandidate(body.ToArray(), body.BitCount, totalBits, imdctOutput, error, risky);
    }

    private static bool IsRiskyCandidate(FrameError error)
    {
        if (error.TargetRms < 0.02 && error.OutputRms > Math.Max(error.TargetRms * 4.0, 0.015))
            return true;

        return error.OutputRms > 0.95;
    }

    private static double CalculateFutureOverlapError(float[][] actual, float[][] desired, int length)
    {
        int start = length / 2;
        double error = 0;
        for (int ch = 0; ch < actual.Length; ch++)
        {
            for (int i = start; i < length; i++)
            {
                double diff = actual[ch][i] - desired[ch][i];
                error += diff * diff;
            }
        }

        return error / (start * actual.Length);
    }

    private static bool IsBetterCandidate(FrameCandidate candidate, FrameCandidate? best)
    {
        if (best is null)
            return true;
        if (candidate.Risky != best.Risky)
            return !candidate.Risky;
        if (candidate.Error < best.Error * 0.995)
            return true;
        return candidate.Error <= best.Error * 1.005 && candidate.TotalBits < best.TotalBits;
    }

    private static double[] GetCandidateCoefficientTargets(double baseTarget, double frameRms)
    {
        return [baseTarget];
    }

    private static bool?[] GetCandidateStereoModes(int channels, double frameRms)
    {
        return [null];
    }

    private static BandTarget[] GetCandidateBandTargets(double frameRms)
    {
        return [OriginalBandTarget];
    }

    private static float[] BuildImdctOutput(float[] target, int frameIndex)
    {
        return BuildImdctOutput(target, frameIndex * SamplesPerFrame, SamplesPerFrame);
    }

    private static float[] BuildImdctOutput(float[] target, int baseSample, int length)
    {
        float[] output = new float[length];
        float[] window = SineWindow.Get(length);
        int nextBase = baseSample + length;

        for (int k = 0; k < length / 2; k++)
        {
            float wi = window[k];
            float wj = window[length - 1 - k];

            float y0 = ReadTarget(target, baseSample + k);
            float y1 = ReadTarget(target, baseSample + length - 1 - k);
            output[(length / 2) - 1 - k] = (-y0 * wi) + (y1 * wj);

            float next0 = ReadTarget(target, nextBase + k);
            float next1 = ReadTarget(target, nextBase + length - 1 - k);
            output[(length / 2) + k] = (next0 * wj) + (next1 * wi);
        }

        return output;
    }

    private static void ApplyStereoTransform(float[] left, float[] right, ScaleBandLayout bands, bool[] transformBands)
    {
        for (int band = 0; band < bands.Count; band++)
        {
            int start = bands.Offsets[band];
            int end = bands.Offsets[band + 1];
            if (transformBands[band])
            {
                for (int i = start; i < end; i++)
                {
                    float l = left[i];
                    float r = right[i];
                    left[i] = (l + r) * 0.5f;
                    right[i] = (r - l) * 0.5f;
                }
            }
            else
            {
                for (int i = start; i < end; i++)
                {
                    left[i] *= UntransformedBandScale;
                    right[i] *= UntransformedBandScale;
                }
            }
        }
    }

    private static bool[]? ChooseStereoTransformBands(float[] left, float[] right, ScaleBandLayout bands, float[][] target, int baseSample)
    {
        double dot = 0;
        double leftEnergy = 0;
        double rightEnergy = 0;
        double sideEnergy = 0;
        double midEnergy = 0;

        for (int i = 0; i < bands.Length; i++)
        {
            float l = ReadTarget(target[0], baseSample + i);
            float r = ReadTarget(target[1], baseSample + i);
            dot += l * r;
            leftEnergy += l * l;
            rightEnergy += r * r;
            float mid = l + r;
            float side = r - l;
            midEnergy += mid * mid;
            sideEnergy += side * side;
        }

        double energy = Math.Sqrt(leftEnergy * rightEnergy);
        if (energy <= 1e-12)
            return CreateAllTransformBands(bands.Count);

        double correlation = dot / energy;
        double frameRms = Math.Sqrt((leftEnergy + rightEnergy) / (bands.Length * 2));
        if (frameRms < 0.025)
            return null;

        return correlation > 0.5 && sideEnergy < midEnergy
            ? CreateAllTransformBands(bands.Count)
            : null;
    }

    private static bool[] CreateAllTransformBands(int bandCount)
    {
        bool[] transformBands = new bool[bandCount];
        Array.Fill(transformBands, true, 0, bandCount);
        return transformBands;
    }

    private static double GetFrameRms(float[][] target, int channels, int frameIndex)
    {
        int start = frameIndex * SamplesPerFrame;
        double energy = 0;
        for (int i = 0; i < SamplesPerFrame; i++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                float sample = ReadTarget(target[ch], start + i);
                energy += sample * sample;
            }
        }

        return Math.Sqrt(energy / (SamplesPerFrame * channels));
    }

    private static float[][] ReconstructImdctOutput(ChannelEncoding[] channels, int channelCount, ScaleBandLayout bands, bool[]? stereoTransformBands)
    {
        float[][] spectral = NewFloatMatrix(channelCount, bands.Length);

        for (int ch = 0; ch < channelCount; ch++)
        {
            ChannelEncoding channel = channels[ch];
            for (int i = 0; i < bands.Length; i++)
                spectral[ch][i] = channel.Coefficients[i];
        }

        if (channelCount == 2 && stereoTransformBands is not null)
        {
            for (int band = 0; band < bands.Count; band++)
            {
                int start = bands.Offsets[band];
                int end = bands.Offsets[band + 1];
                if (stereoTransformBands[band])
                {
                    for (int i = start; i < end; i++)
                    {
                        float mid = spectral[0][i];
                        float side = spectral[1][i];
                        spectral[0][i] = mid - side;
                        spectral[1][i] = mid + side;
                    }
                }
                else
                {
                    for (int i = start; i < end; i++)
                    {
                        spectral[0][i] *= 181.0f / 128.0f;
                        spectral[1][i] *= 181.0f / 128.0f;
                    }
                }
            }
        }

        for (int ch = 0; ch < channelCount; ch++)
        {
            ChannelEncoding channel = channels[ch];
            int maxScaleFactor = channel.ScaleFactors[0];
            for (int band = 1; band < bands.Count; band++)
                maxScaleFactor = Math.Max(maxScaleFactor, channel.ScaleFactors[band]);

            for (int band = 0; band < bands.Count; band++)
            {
                int effectiveExponent = channel.QuantStep - (maxScaleFactor - channel.ScaleFactors[band]);
                float quant = QuantScale.Get(effectiveExponent);
                for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
                    spectral[ch][i] *= quant;
            }
        }

        float[][] output = NewFloatMatrix(channelCount, SamplesPerFrame);
        for (int ch = 0; ch < channelCount; ch++)
            output[ch] = Mdct.Inverse(spectral[ch]);

        return output;
    }

    private static double GetFrameCoefficientTarget(float[][] target, int channels, int frameIndex)
        => TargetCoefficientMagnitude;

    private static float ReadTarget(float[] target, int index)
    {
        return (uint)index < (uint)target.Length ? target[index] : 0.0f;
    }

    private static ChannelEncoding QuantizeChannel(float[] spectrum, ScaleBandLayout bands, int quantBoost, double coefficientTarget, BandTarget bandTarget)
    {
        int[] exponents = new int[bands.Count];
        int quantStep = int.MinValue;

        for (int band = 0; band < bands.Count; band++)
        {
            float max = 0;
            for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
                max = Math.Max(max, Math.Abs(spectrum[i]));

            int exponent = max <= 0.0000001f
                ? -96
                : (int)Math.Floor(20.0 * Math.Log10(max / GetBandCoefficientTarget(band, bands.Count, coefficientTarget, bandTarget)));

            exponent += quantBoost + BaseQuantBoost;
            exponent = Math.Clamp(exponent, -96, 120);
            exponents[band] = exponent;
            quantStep = Math.Max(quantStep, exponent);
        }

        if (quantStep == int.MinValue)
            quantStep = 0;

        int[] scaleFactors = new int[bands.Count];
        for (int band = 0; band < bands.Count; band++)
        {
            int scale = 45 - (quantStep - exponents[band]);
            scaleFactors[band] = Math.Clamp(scale, -15, 105);
        }

        LimitScaleFactorDeltas(scaleFactors);
        int maxScaleFactor = scaleFactors[0];
        for (int band = 1; band < bands.Count; band++)
            maxScaleFactor = Math.Max(maxScaleFactor, scaleFactors[band]);

        quantStep += GetQuantStepSafetyBoost(spectrum, bands, quantStep, scaleFactors, maxScaleFactor);

        int[] coefficients = new int[bands.Length];
        for (int band = 0; band < bands.Count; band++)
        {
            int effectiveExponent = quantStep - (maxScaleFactor - scaleFactors[band]);
            float quant = QuantScale.Get(effectiveExponent);
            for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
            {
                int value = quant <= 0 ? 0 : (int)MathF.Round(spectrum[i] / quant);
                coefficients[i] = Math.Clamp(value, -32767, 32767);
            }
        }

        return CreateChannelEncoding(coefficients, scaleFactors, quantStep);
    }

    private static void QuantizeStereoPair(float[] mid, float[] side, ScaleBandLayout bands, int quantBoost, double coefficientTarget, BandTarget bandTarget, ChannelEncoding[] output)
    {
        int[] exponents = new int[bands.Count];
        int quantStep = int.MinValue;

        for (int band = 0; band < bands.Count; band++)
        {
            float max = 0;
            for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
            {
                max = Math.Max(max, Math.Abs(mid[i]));
                max = Math.Max(max, Math.Abs(side[i]));
            }

            int exponent = max <= 0.0000001f
                ? -96
                : (int)Math.Floor(20.0 * Math.Log10(max / GetBandCoefficientTarget(band, bands.Count, coefficientTarget, bandTarget)));

            exponent += quantBoost + BaseQuantBoost;
            exponent = Math.Clamp(exponent, -96, 120);
            exponents[band] = exponent;
            quantStep = Math.Max(quantStep, exponent);
        }

        if (quantStep == int.MinValue)
            quantStep = 0;

        int[] scaleFactors = new int[bands.Count];
        for (int band = 0; band < bands.Count; band++)
        {
            int scale = 45 - (quantStep - exponents[band]);
            scaleFactors[band] = Math.Clamp(scale, -15, 105);
        }

        LimitScaleFactorDeltas(scaleFactors);
        int maxScaleFactor = scaleFactors[0];
        for (int band = 1; band < bands.Count; band++)
            maxScaleFactor = Math.Max(maxScaleFactor, scaleFactors[band]);

        quantStep += Math.Max(
            GetQuantStepSafetyBoost(mid, bands, quantStep, scaleFactors, maxScaleFactor),
            GetQuantStepSafetyBoost(side, bands, quantStep, scaleFactors, maxScaleFactor));

        int[] midCoefficients = new int[bands.Length];
        int[] sideCoefficients = new int[bands.Length];
        for (int band = 0; band < bands.Count; band++)
        {
            int effectiveExponent = quantStep - (maxScaleFactor - scaleFactors[band]);
            float quant = QuantScale.Get(effectiveExponent);
            for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
            {
                midCoefficients[i] = quant <= 0 ? 0 : Math.Clamp((int)MathF.Round(mid[i] / quant), -32767, 32767);
                sideCoefficients[i] = quant <= 0 ? 0 : Math.Clamp((int)MathF.Round(side[i] / quant), -32767, 32767);
            }
        }

        output[0] = CreateChannelEncoding(midCoefficients, (int[])scaleFactors.Clone(), quantStep);
        output[1] = CreateChannelEncoding(sideCoefficients, (int[])scaleFactors.Clone(), quantStep);
    }

    private static int GetQuantStepSafetyBoost(float[] spectrum, ScaleBandLayout bands, int quantStep, int[] scaleFactors, int maxScaleFactor)
    {
        double requiredBoost = 0;
        for (int band = 0; band < bands.Count; band++)
        {
            int effectiveExponent = quantStep - (maxScaleFactor - scaleFactors[band]);
            float quant = QuantScale.Get(effectiveExponent);
            if (quant <= 0)
                continue;

            for (int i = bands.Offsets[band]; i < bands.Offsets[band + 1]; i++)
            {
                double raw = Math.Abs(spectrum[i]) / quant;
                if (raw > MaxQuantizedCoefficient)
                    requiredBoost = Math.Max(requiredBoost, 20.0 * Math.Log10(raw / MaxQuantizedCoefficient));
            }
        }

        return (int)Math.Ceiling(requiredBoost);
    }

    private static ChannelEncoding CreateChannelEncoding(int[] coefficients, int[] scaleFactors, int quantStep)
    {
        return new ChannelEncoding(coefficients, scaleFactors, quantStep);
    }

    private static void LimitScaleFactorDeltas(int[] scaleFactors)
    {
        int previous = 45;
        for (int i = 0; i < scaleFactors.Length; i++)
        {
            int delta = Math.Clamp(scaleFactors[i] - previous, -60, 60);
            scaleFactors[i] = previous + delta;
            previous = scaleFactors[i];
        }
    }

    private static double GetBandCoefficientTarget(int band, int bandCount, double coefficientTarget, BandTarget bandTarget)
    {
        double position = bandCount <= 1 ? 0.0 : band / (double)(bandCount - 1);
        if (position < 0.35)
            return coefficientTarget;
        if (position < 0.65)
            return coefficientTarget * bandTarget.MidScale;
        return coefficientTarget * bandTarget.HighScale;
    }

    private static void WriteFrameBody(BitWriter writer, SubframeEncoding[] subframes, int channelCount)
    {
        writer.WriteBit(true);      // fixed channel layout
        int writtenSamples = 0;
        for (int i = 0; i < subframes.Length; i++)
        {
            WriteSubframeLength(writer, subframes[i].Bands.Length, writtenSamples);
            writtenSamples += subframes[i].Bands.Length;
        }

        if (channelCount > 1)
            writer.WriteBit(false); // no post-processing transform
        writer.WriteBits(0, 8);     // DRC gain
        writer.WriteBit(false);     // no trim info

        int[][] scaleFactorState = new int[channelCount][];
        for (int i = 0; i < subframes.Length; i++)
        {
            SubframeEncoding subframe = subframes[i];
            WriteSubframeBody(
                writer,
                subframe.Channels,
                channelCount,
                subframe.Bands,
                subframe.StereoTransformBands,
                i == 0,
                scaleFactorState);

            for (int ch = 0; ch < channelCount; ch++)
                scaleFactorState[ch] = (int[])subframe.Channels[ch].ScaleFactors.Clone();
        }
    }

    private static void WriteSubframeLength(BitWriter writer, int length, int writtenSamples)
    {
        if (writtenSamples == SamplesPerFrame - MinSubframeSamples)
            return;

        int shift = BitOperations.Log2((uint)(SamplesPerFrame / length));
        if ((SamplesPerFrame >> shift) != length || length < MinSubframeSamples || length > SamplesPerFrame)
            throw new InvalidDataException($"Unsupported XMA2 subframe length {length}.");

        if (shift == 0)
        {
            writer.WriteBit(false);
            return;
        }

        writer.WriteBit(true);
        int log2MaxSubframes = BitOperations.Log2((uint)(SamplesPerFrame / MinSubframeSamples));
        int subframeLenBits = BitOperations.Log2((uint)log2MaxSubframes) + 1;
        writer.WriteBits(shift - 1, subframeLenBits - 1);
    }

    private static void WriteSubframeBody(
        BitWriter writer,
        ChannelEncoding[] channels,
        int channelCount,
        ScaleBandLayout bands,
        bool[]? stereoTransformBands,
        bool firstSubframe,
        int[][] previousScaleFactors)
    {
        writer.WriteBit(false);     // no extended subframe header
        writer.WriteBit(false);     // reserved bit

        if (channelCount > 1)
        {
            writer.WriteBit(false); // normal channel transform path
            if (stereoTransformBands is null)
            {
                writer.WriteBit(true);
                writer.WriteBit(false);
            }
            else
            {
                writer.WriteBit(false);
                if (AllTrue(stereoTransformBands, bands.Count))
                {
                    writer.WriteBit(true); // transform every scale-factor band
                }
                else
                {
                    writer.WriteBit(false);
                    for (int i = 0; i < bands.Count; i++)
                        writer.WriteBit(stereoTransformBands[i]);
                }
            }
        }

        for (int ch = 0; ch < channelCount; ch++)
            writer.WriteBit(true);  // transmit coefficients

        writer.WriteBit(true);      // transmit number of vector coefficients
        int numVectorBits = BitOperations.Log2((uint)((bands.Length + 3) / 4)) + 1;
        for (int ch = 0; ch < channelCount; ch++)
            writer.WriteBits(bands.Length / 4, numVectorBits);

        int baseQuantStep = GetBaseQuantStep(channels, channelCount);
        WriteQuantStep(writer, baseQuantStep);

        if (channelCount == 1)
        {
            WriteScaleFactors(writer, previousScaleFactors[0], channels[0].ScaleFactors, bands.Count, firstSubframe);
        }
        else
        {
            WriteChannelQuantModifiers(writer, channels, channelCount, baseQuantStep);

            for (int ch = 0; ch < channelCount; ch++)
                WriteScaleFactors(writer, previousScaleFactors[ch], channels[ch].ScaleFactors, bands.Count, firstSubframe);
        }

        for (int ch = 0; ch < channelCount; ch++)
            WriteCoefficients(writer, channels[ch].Coefficients);
    }

    private static bool AllTrue(bool[] values, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!values[i])
                return false;
        }

        return true;
    }

    private static int GetBaseQuantStep(ChannelEncoding[] channels, int channelCount)
    {
        int quantStep = channels[0].QuantStep;
        for (int ch = 1; ch < channelCount; ch++)
            quantStep = Math.Min(quantStep, channels[ch].QuantStep);
        return quantStep;
    }

    private static void WriteChannelQuantModifiers(BitWriter writer, ChannelEncoding[] channels, int channelCount, int baseQuantStep)
    {
        int maxDiff = 0;
        for (int ch = 0; ch < channelCount; ch++)
            maxDiff = Math.Max(maxDiff, channels[ch].QuantStep - baseQuantStep);

        int modifierLen = maxDiff <= 1 ? 0 : BitOperations.Log2((uint)(maxDiff - 1)) + 1;
        if (modifierLen > 7)
            throw new InvalidDataException("XMA2 channel quantizer difference exceeds the encodable range.");

        writer.WriteBits(modifierLen, 3);
        for (int ch = 0; ch < channelCount; ch++)
        {
            int diff = channels[ch].QuantStep - baseQuantStep;
            writer.WriteBit(diff != 0);
            if (diff != 0 && modifierLen != 0)
                writer.WriteBits(diff - 1, modifierLen);
        }
    }

    private static void WriteQuantStep(BitWriter writer, int quantStep)
    {
        // -32 and 31 are escape sentinels in the 6-bit delta field.
        if (quantStep is >= 59 and <= 120)
        {
            writer.WriteSignedBits(quantStep - 90, 6);
            return;
        }

        if (quantStep < 59)
        {
            writer.WriteSignedBits(-32, 6);
            int delta = 58 - quantStep;
            while (delta >= 31)
            {
                writer.WriteBits(31, 5);
                delta -= 31;
            }

            writer.WriteBits(delta, 5);
            return;
        }

        writer.WriteSignedBits(31, 6);
        int extra = quantStep - 121;
        while (extra >= 31)
        {
            writer.WriteBits(31, 5);
            extra -= 31;
        }

        writer.WriteBits(extra, 5);
    }

    private static void WriteScaleFactors(BitWriter writer, int[]? previousScaleFactors, int[] scaleFactors, int bandCount, bool firstSubframe)
    {
        if (!firstSubframe)
        {
            if (previousScaleFactors is null)
                throw new InvalidDataException("XMA2 scale factor state is missing for a reused subframe.");

            bool changed = false;
            for (int i = 0; i < bandCount; i++)
            {
                if (scaleFactors[i] != previousScaleFactors[i])
                {
                    changed = true;
                    break;
                }
            }

            writer.WriteBit(changed);
            if (changed)
                WriteScaleFactorRunLevelDeltas(writer, previousScaleFactors, scaleFactors, bandCount);
            return;
        }

        writer.WriteBits(0, 2); // scale factor step = 1
        int previous = 45;
        for (int i = 0; i < bandCount; i++)
        {
            int delta = Math.Clamp(scaleFactors[i] - previous, -60, 60);
            ScaleCodes.Write(writer, delta);
            previous += delta;
        }
    }

    private static void WriteScaleFactorRunLevelDeltas(BitWriter writer, int[] previousScaleFactors, int[] scaleFactors, int bandCount)
    {
        int cursor = 0;
        while (cursor < bandCount)
        {
            int changedBand = cursor;
            while (changedBand < bandCount && scaleFactors[changedBand] == previousScaleFactors[changedBand])
                changedBand++;

            if (changedBand >= bandCount)
            {
                ScaleRunLevelCodes.Write(writer, 1); // end of scale-factor delta list
                break;
            }

            int skip = changedBand - cursor;
            int delta = scaleFactors[changedBand] - previousScaleFactors[changedBand];
            WriteScaleFactorRunLevelDelta(writer, skip, delta);
            cursor = changedBand + 1;
        }
    }

    private static void WriteScaleFactorRunLevelDelta(BitWriter writer, int skip, int delta)
    {
        int level = Math.Abs(delta);
        if (skip > 31 || level == 0 || level > 255)
            throw new InvalidDataException("XMA2 scale-factor delta is outside the encodable range.");

        ScaleRunLevelCodes.Write(writer, 0);
        int sign = delta > 0 ? 1 : 0;
        writer.WriteBits((level << 6) | (skip << 1) | sign, 14);
    }

    private static void WriteCoefficients(BitWriter writer, int[] coefficients)
    {
        writer.WriteBit(false); // coefficient table 0
        int[] magnitudes = new int[4];
        bool[] signs = new bool[4];

        for (int i = 0; i < coefficients.Length; i += 4)
        {
            for (int j = 0; j < 4; j++)
            {
                int value = coefficients[i + j];
                signs[j] = value >= 0;
                magnitudes[j] = Math.Abs(value);
            }

            WriteVector4(writer, magnitudes);

            for (int j = 0; j < 4; j++)
            {
                if (magnitudes[j] != 0)
                    writer.WriteBit(signs[j]);
            }
        }
    }

    private static void WriteVector4(BitWriter writer, ReadOnlySpan<int> magnitudes)
    {
        if (magnitudes[0] <= 15 && magnitudes[1] <= 15 && magnitudes[2] <= 15 && magnitudes[3] <= 15)
        {
            int packed = (magnitudes[0] << 12) | (magnitudes[1] << 8) | (magnitudes[2] << 4) | magnitudes[3];
            if (Vector4Codes.TryWrite(writer, packed))
                return;
        }

        Vector4Codes.Write(writer, -1);
        WriteVector2(writer, magnitudes[0], magnitudes[1]);
        WriteVector2(writer, magnitudes[2], magnitudes[3]);
    }

    private static void WriteVector2(BitWriter writer, int first, int second)
    {
        if (first <= 15 && second <= 15)
        {
            int packed = (first << 4) | second;
            if (Vector2Codes.TryWrite(writer, packed))
                return;
        }

        Vector2Codes.Write(writer, -1);
        WriteVector1(writer, first);
        WriteVector1(writer, second);
    }

    private static void WriteVector1(BitWriter writer, int value)
    {
        if (value < Xma2Tables.HuffVec1Size - 1)
        {
            Vector1Codes.Write(writer, value);
            return;
        }

        Vector1Codes.Write(writer, Xma2Tables.HuffVec1Size - 1);
        WriteLargeValue(writer, value - (Xma2Tables.HuffVec1Size - 1));
    }

    private static void WriteLargeValue(BitWriter writer, int value)
    {
        if (value < 0x100)
        {
            writer.WriteBit(false);
            writer.WriteBits(value, 8);
        }
        else if (value < 0x10000)
        {
            writer.WriteBit(true);
            writer.WriteBit(false);
            writer.WriteBits(value, 16);
        }
        else if (value < 0x1000000)
        {
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(false);
            writer.WriteBits(value, 24);
        }
        else
        {
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBits(value, 31);
        }
    }

    private static byte[] Packetize(List<EncodedFrame> frames)
    {
        MemoryStream stream = new();
        int frameIndex = 0;
        int frameBitOffset = 0;
        const int headerBits = 32;
        const int payloadBits = (PacketSize * 8) - headerBits;

        while (frameIndex < frames.Count)
        {
            BitWriter payload = new();
            int previousFrameBits = 0;
            int completedFrames = 0;

            if (frameBitOffset > 0)
            {
                EncodedFrame frame = frames[frameIndex];
                int bitsToWrite = Math.Min(frame.TotalBits - frameBitOffset, payloadBits);
                bool completesFrame = frameBitOffset + bitsToWrite == frame.TotalBits;
                bool moreFrames = completesFrame &&
                    frameIndex + 1 < frames.Count &&
                    payloadBits - bitsToWrite > 15;

                WriteFrameBits(payload, frame, frameBitOffset, bitsToWrite, moreFrames);
                previousFrameBits = bitsToWrite;
                frameBitOffset += bitsToWrite;

                if (completesFrame)
                {
                    frameIndex++;
                    frameBitOffset = 0;
                    completedFrames++;
                    if (!moreFrames)
                        WritePacket(stream, payload, previousFrameBits, completedFrames);
                }
            }

            while (frameIndex < frames.Count && payload.BitCount + 15 < payloadBits)
            {
                EncodedFrame frame = frames[frameIndex];
                int remaining = payloadBits - payload.BitCount;
                if (frame.TotalBits <= remaining)
                {
                    bool moreFrames = frameIndex + 1 < frames.Count &&
                        remaining - frame.TotalBits > 15;
                    WriteFrameBits(payload, frame, 0, frame.TotalBits, moreFrames);
                    frameIndex++;
                    completedFrames++;
                    if (!moreFrames)
                        break;
                }
                else
                {
                    WriteFrameBits(payload, frame, 0, remaining, moreFrames: false);
                    frameBitOffset = remaining;
                    break;
                }
            }

            if (payload.BitCount == 0)
                throw new InvalidDataException("Unable to pack XMA2 frame bits.");

            WritePacket(stream, payload, previousFrameBits, completedFrames);
        }

        return stream.ToArray();
    }

    private static void WritePacket(Stream stream, BitWriter payload, int previousFrameBits, int completedFrames)
    {
        BitWriter packet = new();
        packet.WriteBits(Math.Min(completedFrames, 63), 6);
        packet.WriteBits(previousFrameBits, 15);
        packet.WriteBits(1, 3);
        packet.WriteBits(0, 8);
        packet.WriteBits(payload.ToArray(), payload.BitCount);
        byte[] packetBytes = packet.ToArray(PacketSize);
        stream.Write(packetBytes);
    }

    private static void WriteFrameBits(BitWriter writer, EncodedFrame frame, int bitOffset, int bitCount, bool moreFrames)
    {
        for (int i = 0; i < bitCount; i++)
            writer.WriteBit(GetFrameBit(frame, bitOffset + i, moreFrames));
    }

    private static bool GetFrameBit(EncodedFrame frame, int bitIndex, bool moreFrames)
    {
        if (bitIndex < 15)
            return ((frame.TotalBits >> (14 - bitIndex)) & 1) != 0;

        bitIndex -= 15;
        if (bitIndex < frame.BodyBits)
        {
            int byteIndex = bitIndex >> 3;
            int bitInByte = 7 - (bitIndex & 7);
            return ((frame.Body[byteIndex] >> bitInByte) & 1) != 0;
        }

        return moreFrames;
    }

    private sealed record ChannelEncoding(int[] Coefficients, int[] ScaleFactors, int QuantStep);

    private readonly record struct BandTarget(double MidScale, double HighScale);

    private sealed record SubframeEncoding(ChannelEncoding[] Channels, bool[]? StereoTransformBands, ScaleBandLayout Bands);

    private sealed record EncodedFrame(byte[] Body, int BodyBits, int TotalBits);

    private readonly record struct FrameError(double Error, double TargetRms, double OutputRms);

    private sealed record FrameCandidate(byte[] Body, int BodyBits, int TotalBits, float[][] ImdctOutput, double Error, bool Risky);

    private sealed class FrameAnalysisState
    {
        private readonly float[][] _previousOverlap;

        public FrameAnalysisState(int channels)
        {
            _previousOverlap = NewFloatMatrix(channels, SamplesPerFrame / 2);
        }

        public FrameError MeasureError(float[][] imdctOutput, float[][] target, int frameIndex)
        {
            double outputEnergy = 0;
            double targetEnergy = 0;
            double errorEnergy = 0;
            float[] window = SineWindow.Get(SamplesPerFrame);
            int start = frameIndex * SamplesPerFrame;

            for (int ch = 0; ch < imdctOutput.Length; ch++)
            {
                for (int k = 0; k < SamplesPerFrame / 2; k++)
                {
                    float previous = _previousOverlap[ch][k];
                    float current = imdctOutput[ch][(SamplesPerFrame / 2) - 1 - k];
                    float wi = window[k];
                    float wj = window[SamplesPerFrame - 1 - k];

                    double first = (previous * wj) - (current * wi);
                    double second = (previous * wi) + (current * wj);
                    double targetFirst = ReadTarget(target[ch], start + k);
                    double targetSecond = ReadTarget(target[ch], start + SamplesPerFrame - 1 - k);
                    double firstError = first - targetFirst;
                    double secondError = second - targetSecond;

                    outputEnergy += (first * first) + (second * second);
                    targetEnergy += (targetFirst * targetFirst) + (targetSecond * targetSecond);
                    errorEnergy += (firstError * firstError) + (secondError * secondError);
                }
            }

            double outputRms = Math.Sqrt(outputEnergy / (SamplesPerFrame * imdctOutput.Length));
            double targetRms = Math.Sqrt(targetEnergy / (SamplesPerFrame * imdctOutput.Length));
            double rmsPenalty = Math.Abs(outputRms - targetRms) * 0.1;
            double error = (errorEnergy / (SamplesPerFrame * imdctOutput.Length)) + rmsPenalty;
            return new FrameError(error, targetRms, outputRms);
        }

        public void Commit(float[][] imdctOutput)
        {
            for (int ch = 0; ch < imdctOutput.Length; ch++)
                Array.Copy(imdctOutput[ch], SamplesPerFrame / 2, _previousOverlap[ch], 0, SamplesPerFrame / 2);
        }
    }

    private sealed class ScaleBandLayout
    {
        private ScaleBandLayout(int[] offsets, int count, int length)
        {
            Offsets = offsets;
            Count = count;
            Length = length;
        }

        public int[] Offsets { get; }

        public int Count { get; }

        public int Length { get; }

        public static ScaleBandLayout Create(int sampleRate, int length = SamplesPerFrame)
        {
            int[] offsets = new int[MaxBands];
            int band = 1;
            int rate = GetCodecRate(sampleRate);
            offsets[0] = 0;

            for (int x = 0; x < MaxBands - 1 && offsets[band - 1] < length; x++)
            {
                int offset = (length * 2 * Xma2Tables.CriticalFrequencies[x]) / rate + 2;
                offset &= ~3;
                if (offset > offsets[band - 1])
                    offsets[band++] = offset;

                if (offset >= length)
                    break;
            }

            offsets[band - 1] = length;
            return new ScaleBandLayout(offsets, band - 1, length);
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

    private static class SineWindow
    {
        private static readonly Dictionary<int, float[]> Cache = [];

        public static float[] Get(int length)
        {
            if (Cache.TryGetValue(length, out float[]? window))
                return window;

            window = Create(length);
            Cache[length] = window;
            return window;
        }

        private static float[] Create(int length)
        {
            float[] window = new float[length];
            for (int i = 0; i < window.Length; i++)
                window[i] = (float)Math.Sin((i + 0.5) * (Math.PI / (2.0 * length)));
            return window;
        }
    }

    private static class Mdct
    {
        private static readonly Dictionary<int, float[][]> ForwardMatrices = [];
        private static readonly Dictionary<int, (float[][] Down, float[][] Up)> InverseMatrices = [];

        public static float[] Forward(float[] imdctOutput)
        {
            int length = imdctOutput.Length;
            float[][] matrix = GetForwardMatrix(length);
            float[] coefficients = new float[length];
            for (int i = 0; i < length; i++)
                coefficients[i] = Dot(imdctOutput, matrix[i], length);
            return coefficients;
        }

        public static float[] Inverse(float[] coefficients)
        {
            int length = coefficients.Length;
            var matrices = GetInverseMatrices(length);
            float[] output = new float[length];
            int half = length >> 1;
            for (int i = 0; i < half; i++)
                output[i] = Dot(coefficients, matrices.Down[i], length);
            for (int i = 0; i < half; i++)
                output[half + i] = Dot(coefficients, matrices.Up[i], length);
            return output;
        }

        private static float[][] GetForwardMatrix(int length)
        {
            if (ForwardMatrices.TryGetValue(length, out float[][]? existing))
                return existing;

            float[][] matrix = NewFloatMatrix(length, length);
            int half = length >> 1;
            double phase = Math.PI / (4.0 * length);
            double scale = 32768.0;

            for (int j = 0; j < length; j++)
            {
                double a = (2 * j) + 1;
                for (int i = 0; i < half; i++)
                {
                    double downPhase = phase * ((2 * length) - (2 * i) - 1);
                    double upPhase = phase * ((3 * length) + (2 * i) + 1);
                    matrix[j][i] = (float)(Math.Cos(a * downPhase) * scale);
                    matrix[j][half + i] = (float)(-Math.Cos(a * upPhase) * scale);
                }
            }

            ForwardMatrices[length] = matrix;
            return matrix;
        }

        private static (float[][] Down, float[][] Up) GetInverseMatrices(int length)
        {
            if (InverseMatrices.TryGetValue(length, out var existing))
                return existing;

            var created = (CreateInverseMatrix(length, up: false), CreateInverseMatrix(length, up: true));
            InverseMatrices[length] = created;
            return created;
        }

        private static float[][] CreateInverseMatrix(int length, bool up)
        {
            float[][] matrix = NewFloatMatrix(length / 2, length);
            int half = length >> 1;
            double phase = Math.PI / (4.0 * length);
            double scale = 1.0 / half / (1 << 15);

            for (int i = 0; i < half; i++)
            {
                double blockPhase = phase * (up
                    ? (3 * length) + (2 * i) + 1
                    : (2 * length) - (2 * i) - 1);

                for (int j = 0; j < length; j++)
                {
                    double a = (2 * j) + 1;
                    double value = Math.Cos(a * blockPhase) * scale;
                    matrix[i][j] = (float)(up ? -value : value);
                }
            }

            return matrix;
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
    }

    private static class QuantScale
    {
        private const int MinExponent = -128;
        private const int MaxExponent = 160;

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

    private sealed class VlcCodebook
    {
        private readonly Dictionary<int, VlcCode> _codes;

        private VlcCodebook(Dictionary<int, VlcCode> codes)
        {
            _codes = codes;
        }

        public static VlcCodebook FromLengths(ReadOnlySpan<byte> lengths, ReadOnlySpan<ushort> symbols, int offset)
        {
            Dictionary<int, VlcCode> codes = [];
            ulong code = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                int len = lengths[i];
                if (len > 0)
                {
                    int symbol = (symbols.Length > 0 ? symbols[i] : i) + offset;
                    codes[symbol] = new VlcCode((uint)(code >> (32 - len)), len);
                    code += 1UL << (32 - len);
                }
            }

            return new VlcCodebook(codes);
        }

        public static VlcCodebook FromTable(ReadOnlySpan<(ushort Symbol, byte Length)> table, int offset)
        {
            Dictionary<int, VlcCode> codes = [];
            ulong code = 0;
            for (int i = 0; i < table.Length; i++)
            {
                int len = table[i].Length;
                if (len > 0)
                {
                    codes[table[i].Symbol + offset] = new VlcCode((uint)(code >> (32 - len)), len);
                    code += 1UL << (32 - len);
                }
            }

            return new VlcCodebook(codes);
        }

        public bool TryWrite(BitWriter writer, int symbol)
        {
            if (!_codes.TryGetValue(symbol, out VlcCode code))
                return false;

            writer.WriteBits(code.Code, code.Length);
            return true;
        }

        public bool TryGetLength(int symbol, out int length)
        {
            if (_codes.TryGetValue(symbol, out VlcCode code))
            {
                length = code.Length;
                return true;
            }

            length = 0;
            return false;
        }

        public void Write(BitWriter writer, int symbol)
        {
            if (!TryWrite(writer, symbol))
                throw new InvalidDataException($"No XMA2 VLC code for symbol {symbol}.");
        }
    }

    private readonly record struct VlcCode(uint Code, int Length);

    private sealed class BitWriter
    {
        private readonly List<byte> _data = [];

        public int BitCount { get; private set; }

        public void WriteBit(bool bit)
        {
            int bitOffset = BitCount & 7;
            if (bitOffset == 0)
                _data.Add(0);

            if (bit)
                _data[^1] |= (byte)(1 << (7 - bitOffset));

            BitCount++;
        }

        public void WriteBits(int value, int count) => WriteBits((uint)value, count);

        public void WriteBits(uint value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
                WriteBit(((value >> bit) & 1) != 0);
        }

        public void WriteBits(byte[] source, int bitCount)
        {
            for (int i = 0; i < bitCount; i++)
            {
                int byteIndex = i >> 3;
                int bitIndex = 7 - (i & 7);
                WriteBit(((source[byteIndex] >> bitIndex) & 1) != 0);
            }
        }

        public void WriteSignedBits(int value, int count)
        {
            WriteBits((uint)(value & ((1 << count) - 1)), count);
        }

        public byte[] ToArray() => [.. _data];

        public byte[] ToArray(int byteCount)
        {
            byte[] output = new byte[byteCount];
            byte[] source = ToArray();
            Array.Copy(source, output, Math.Min(source.Length, output.Length));
            return output;
        }
    }

    private static float[][] NewFloatMatrix(int rows, int columns)
    {
        float[][] matrix = new float[rows][];
        for (int i = 0; i < rows; i++)
            matrix[i] = new float[columns];
        return matrix;
    }
}
