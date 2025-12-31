using LibVLCSharp.Shared;

using System;
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
}