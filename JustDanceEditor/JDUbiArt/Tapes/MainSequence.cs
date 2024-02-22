using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.JDUbiArt.Tapes;

public class MainSequence
{
    public string __class { get; set; }
    public AudioVibrationClip[] Clips { get; set; }
    public int TapeClock { get; set; }
    public int TapeBarCount { get; set; }
    public int FreeResourcesAfterPlay { get; set; }
    public string MapName { get; set; }
    public string SoundwichEvent { get; set; }
}

public class AudioVibrationClip
{
    public string __class { get; set; }
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string SoundSetPath { get; set; }
    public int SoundChannel { get; set; }
    public int StartOffset { get; set; }
    public int StopsOnEnd { get; set; }
    public int AccountedForDuration { get; set; }
    public string VibrationFilePath { get; set; }
    public int Loop { get; set; }
    public int DeviceSide { get; set; }
    public int PlayerId { get; set; }
    public int Context { get; set; }
    public int StartTimeOffset { get; set; }
    public float Modulation { get; set; }
}
