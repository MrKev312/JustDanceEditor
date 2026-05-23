namespace JustDanceEditor.Formats.JDI.Services;

public sealed record JdiAudioEncodeRequest(string SourcePath)
{
    public string? OutputFormat { get; init; }
    public string? Codec { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public string? Bitrate { get; init; }
    public string? SampleFormat { get; init; }
    public TimeSpan? FadeInDuration { get; init; }
    public TimeSpan? FadeOutStart { get; init; }
    public TimeSpan? FadeOutDuration { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool OverwriteOutput { get; init; } = true;
}
