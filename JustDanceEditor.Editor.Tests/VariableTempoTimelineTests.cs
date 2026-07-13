using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Views.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using NAudio.Wave;

namespace JustDanceEditor.Editor.Tests;

public sealed class VariableTempoTimelineTests
{
    [Fact]
    public void WaveformSampleRanges_FollowMarkerTimingAcrossTempoChange()
    {
        TimelineStructureDocument structure = CreateStructure();
        double duration = structure.GetPlaybackSecondsAtBeat(structure.EndBeat);

        (int slowStart, int slowEnd) = AudioBarRenderer.GetWaveformSampleRange(
            structure, 1, 2, duration, sampleCount: 4500);
        (int fastStart, int fastEnd) = AudioBarRenderer.GetWaveformSampleRange(
            structure, 2, 3, duration, sampleCount: 4500);

        Assert.Equal((2500, 3500), (slowStart, slowEnd));
        Assert.Equal((3500, 4000), (fastStart, fastEnd));
    }

    [Fact]
    public void WaveformEnvelopeTiles_CoverOnlyVisiblePixelRange()
    {
        (int firstTile, int lastTile) = AudioBarRenderer.GetEnvelopeTileRange(1600, 3000);

        Assert.Equal(3, firstTile);
        Assert.Equal(5, lastTile);
        Assert.Equal(3, lastTile - firstTile + 1);
    }

    [Fact]
    public void MetronomeClicks_FollowMarkerTimingAcrossTempoChange()
    {
        TimelineStructureDocument structure = CreateStructure();
        VariableMetronomeSampleProvider metronome = new(new SilentSampleProvider(sampleRate: 8000))
        {
            Enabled = true
        };
        metronome.UpdateTiming(
            structure.GetPlaybackSecondsAtBeat(0),
            bpm: 60,
            beatsPerMeasure: 4,
            sectionStarts: null,
            structure.GetPlaybackSecondsAtBeat,
            structure.GetBeatAtPlaybackSeconds);
        float[] samples = new float[32800];

        metronome.Read(samples, 0, samples.Length);

        Assert.Contains(samples[28000..28280], sample => Math.Abs(sample) > 0.05f);
        Assert.Contains(samples[32000..32280], sample => Math.Abs(sample) > 0.05f);
    }

    private static TimelineStructureDocument CreateStructure() => new()
    {
        StartBeat = -2,
        EndBeat = 4,
        Markers = [0, 48000, 96000, 120000, 144000]
    };

    private sealed class SilentSampleProvider(int sampleRate) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}