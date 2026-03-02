using LibVLCSharp.Shared;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public interface IPlaybackService : IDisposable
{
    bool IsPlaying { get; }
    TimeSpan CurrentTime { get; }
    TimeSpan Duration { get; }
    double CurrentBeat { get; }

    // Exposed for the Video View control
    MediaPlayer? MediaPlayer { get; }

    event EventHandler TimeChanged;
    event EventHandler PlayStateChanged;

    Task LoadMediaAsync(
        string audioPath,
        string videoPath,
        Func<double, double> beatToSeconds,
        Func<double, double> secondsToBeat,
        double videoStartOffset);
    void Play();
    void Pause();
    void Seek(TimeSpan time);
    void SeekToBeat(double beat);

    /// <summary>
    /// Enables or disables the metronome click overlay.
    /// </summary>
    bool IsMetronomeEnabled { get; set; }

    /// <summary>
    /// Updates the metronome timing parameters. Safe to call at any time.
    /// </summary>
    void UpdateMetronome(double zeroBeatTimeSeconds, double bpm, int beatsPerMeasure, IEnumerable<double>? sectionStarts = null);

    /// <summary>
    /// Extends the effective duration beyond the audio file end by padding with silence.
    /// The timer will stop at whichever is later: the audio file end or this value.
    /// </summary>
    void SetExtendedEnd(TimeSpan end);
}