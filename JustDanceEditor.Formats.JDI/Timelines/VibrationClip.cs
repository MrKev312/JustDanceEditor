using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class VibrationClip : TimelineClipBase
{
    public long TrackId { get; set; }
    public int Duration { get; set; }
    public string VibrationFilePath { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Loop { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DeviceSide { get; set; }

    public int? PlayerId
    {
        get;
        set => field = value == -1 ? null : value;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Context { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StartTimeOffset { get; set; }

    public float? Modulation
    {
        get;
        set => field = value.HasValue && Math.Abs(value.Value - 0.5f) < 0.000001f ? null : value;
    }
}