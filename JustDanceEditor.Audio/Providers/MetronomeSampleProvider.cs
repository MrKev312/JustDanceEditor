using NAudio.Wave;

namespace JustDanceEditor.Audio.Providers;

/// <summary>
/// An <see cref="ISampleProvider"/> that generates metronome click sounds at beat positions.
/// Produces:
///   - Section click (1600 Hz) on section boundaries
///   - Measure click (1200 Hz) on 4-beat group starts
///   - Beat click  (800 Hz) on regular beats
/// </summary>
public class MetronomeSampleProvider(ISampleProvider source) : ISampleProvider
{
    private readonly int _sampleRate = source.WaveFormat.SampleRate;
    private readonly int _channels = source.WaveFormat.Channels;
    private long _position = 0;

    // Beat timing parameters
    private double _zeroBeatTimeSeconds;
    private double _bpm;
    private int _beatsPerMeasure;
    private readonly List<double> _sectionBeatPositions = [];

    // Click sound parameters
    private const double BeatClickDurationSeconds = 0.025;
    private const double MeasureClickDurationSeconds = 0.035;
    private const double SectionClickDurationSeconds = 0.05;

    private const double BeatClickFrequency = 800.0;
    private const double MeasureClickFrequency = 1200.0;
    private const double SectionClickFrequency = 1600.0;

    private const float BeatClickVolume = 0.25f;
    private const float MeasureClickVolume = 0.4f;
    private const float SectionClickVolume = 0.5f;

    /// <summary>When false, click sounds are not mixed in (pure pass-through).</summary>
    public bool Enabled { get; set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>
    /// Updates the metronome timing parameters. Can be called while playing.
    /// </summary>
    /// <param name="sectionStarts">Beat positions where song sections begin.</param>
    public void UpdateTiming(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null)
    {
        _zeroBeatTimeSeconds = zeroBeatTimeSeconds;
        _bpm = bpm;
        _beatsPerMeasure = beatsPerMeasure;

        _sectionBeatPositions.Clear();
        if (sectionStarts != null)
        {
            foreach (double beat in sectionStarts)
                _sectionBeatPositions.Add(beat);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = source.Read(buffer, offset, count);

        if (!Enabled || _bpm <= 0)
        {
            _position += samplesRead / _channels;
            return samplesRead;
        }

        double beatDuration = 60.0 / _bpm;

        for (int i = 0; i < samplesRead; i += _channels)
        {
            long sampleIndex = _position + (i / _channels);
            double timeSeconds = (double)sampleIndex / _sampleRate;

            double beatPosition = (timeSeconds - _zeroBeatTimeSeconds) / beatDuration;
            double nearestBeat = Math.Round(beatPosition);
            double distanceFromBeat = Math.Abs(beatPosition - nearestBeat);
            double distanceInSeconds = distanceFromBeat * beatDuration;

            float clickSample = 0f;

            bool isSectionBoundary = false;
            foreach (double sectionBeat in _sectionBeatPositions)
            {
                if (Math.Abs(nearestBeat - sectionBeat) < 0.01)
                {
                    isSectionBoundary = true;
                    break;
                }
            }

            if (isSectionBoundary && distanceInSeconds < SectionClickDurationSeconds)
            {
                double t = distanceInSeconds / SectionClickDurationSeconds;
                double envelope = 1.0 - t;
                double phase = 2.0 * Math.PI * SectionClickFrequency * distanceInSeconds;
                clickSample = SectionClickVolume * (float)(envelope * Math.Sin(phase));
            }
            else if (_beatsPerMeasure > 0 && IsMeasureBeat(nearestBeat) &&
                     distanceInSeconds < MeasureClickDurationSeconds)
            {
                double t = distanceInSeconds / MeasureClickDurationSeconds;
                double envelope = 1.0 - t;
                double phase = 2.0 * Math.PI * MeasureClickFrequency * distanceInSeconds;
                clickSample = MeasureClickVolume * (float)(envelope * Math.Sin(phase));
            }
            else if (distanceInSeconds < BeatClickDurationSeconds)
            {
                double t = distanceInSeconds / BeatClickDurationSeconds;
                double envelope = 1.0 - t;
                double phase = 2.0 * Math.PI * BeatClickFrequency * distanceInSeconds;
                clickSample = BeatClickVolume * (float)(envelope * Math.Sin(phase));
            }

            for (int ch = 0; ch < _channels && (i + ch) < samplesRead; ch++)
                buffer[offset + i + ch] += clickSample;
        }

        _position += samplesRead / _channels;
        return samplesRead;
    }

    /// <summary>
    /// Resets the internal position counter. Call this when seeking in the audio.
    /// </summary>
    public void ResetPosition(double timeSeconds)
    {
        _position = (long)(timeSeconds * _sampleRate);
    }

    private bool IsMeasureBeat(double beat)
    {
        const int baseGroup = 4;

        double sectionStart = 0;
        for (int i = _sectionBeatPositions.Count - 1; i >= 0; i--)
        {
            if (_sectionBeatPositions[i] <= beat + 0.01)
            {
                sectionStart = _sectionBeatPositions[i];
                break;
            }
        }

        double rel = beat - sectionStart;
        return Math.Abs(rel % baseGroup) < 0.01 || Math.Abs((rel % baseGroup) - baseGroup) < 0.01;
    }
}
