// File: .\Services\Lyrics\LyricLine.cs

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// A single karaoke clip produced by a lyric importer.
/// </summary>
/// <param name="StartSeconds">Absolute song-time in seconds at which this syllable starts.</param>
/// <param name="EndSeconds">
/// Absolute song-time in seconds signalling the next syllable (= end of this clip's
/// visible range).  When the importer cannot determine an end time, set this equal to
/// <see cref="StartSeconds"/> and the import command will apply a default duration.
/// </param>
/// <param name="Text">Syllable / word text.</param>
/// <param name="IsEndOfLine">True when this is the last syllable in a lyric line.</param>
public record LyricLine(double StartSeconds, double EndSeconds, string Text, bool IsEndOfLine);