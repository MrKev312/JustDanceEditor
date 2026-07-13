using NAudio.Wave;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Mixes timeline clicks using the same beat/time conversion as the playhead.
/// </summary>
internal sealed class VariableMetronomeSampleProvider(ISampleProvider source) : ISampleProvider
{
    private const double ClickDurationSeconds = 0.035;
    private readonly Lock _gate = new();
    private readonly int _sampleRate = source.WaveFormat.SampleRate;
    private readonly int _channels = source.WaveFormat.Channels;
    private long _position;
    private Func<double, double> _beatToSeconds = static beat => beat * 0.5;
    private Func<double, double> _secondsToBeat = static seconds => seconds * 2;
    private int _beatsPerMeasure = 4;
    private HashSet<int> _sectionStartBeats = [];

    public bool Enabled { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public void UpdateTiming(
        double zeroBeatTimeSeconds,
        double bpm,
        int beatsPerMeasure,
        IEnumerable<double>? sectionStarts,
        Func<double, double>? beatToSeconds = null,
        Func<double, double>? secondsToBeat = null)
    {
        double secondsPerBeat = 60.0 / (bpm > 0 ? bpm : 120.0);
        lock (_gate)
        {
            _beatToSeconds = beatToSeconds ?? (beat => zeroBeatTimeSeconds + (beat * secondsPerBeat));
            _secondsToBeat = secondsToBeat ?? (seconds => (seconds - zeroBeatTimeSeconds) / secondsPerBeat);
            _beatsPerMeasure = Math.Max(1, beatsPerMeasure);
            _sectionStartBeats = sectionStarts?.Select(value => (int)Math.Round(value)).ToHashSet() ?? [];
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = source.Read(buffer, offset, count);
        if (!Enabled)
        {
            _position += samplesRead / _channels;
            return samplesRead;
        }

        Func<double, double> beatToSeconds;
        Func<double, double> secondsToBeat;
        int beatsPerMeasure;
        HashSet<int> sectionStartBeats;
        lock (_gate)
        {
            beatToSeconds = _beatToSeconds;
            secondsToBeat = _secondsToBeat;
            beatsPerMeasure = _beatsPerMeasure;
            sectionStartBeats = _sectionStartBeats;
        }

        for (int i = 0; i < samplesRead; i += _channels)
        {
            double seconds = (_position + (i / _channels)) / (double)_sampleRate;
            int beat = (int)Math.Floor(secondsToBeat(seconds) + 1e-9);
            double clickTime = seconds - beatToSeconds(beat);
            if (clickTime is < 0 or >= ClickDurationSeconds)
                continue;

            bool isSection = sectionStartBeats.Contains(beat);
            bool isMeasure = Mod(beat, beatsPerMeasure) == 0;
            double frequency = isSection ? 1800 : isMeasure ? 1400 : 1000;
            double gain = isSection ? 0.35 : isMeasure ? 0.28 : 0.20;
            double envelope = 1.0 - (clickTime / ClickDurationSeconds);
            float click = (float)(Math.Sin(2 * Math.PI * frequency * clickTime) * gain * envelope);
            for (int channel = 0; channel < _channels && i + channel < samplesRead; channel++)
                buffer[offset + i + channel] += click;
        }

        _position += samplesRead / _channels;
        return samplesRead;
    }

    public void ResetPosition(double timeSeconds) =>
        _position = (long)Math.Round(timeSeconds * _sampleRate);

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}