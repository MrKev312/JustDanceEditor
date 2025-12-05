using JustDanceEditor.Formats.JDI.Timelines;

using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Unity.Models;

public sealed class UnityExportData(
    string name,
    UnityExportMetadata metadata,
    TimelineStructureDocument structure,
    IReadOnlyList<KaraokeClip> karaokeClips,
    IReadOnlyList<PictogramEntry> pictogramClips,
    IReadOnlyList<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> motionClips,
    IReadOnlyList<GoldEffectTimelineClip> goldEffectClips,
    IReadOnlyList<HideUserInterfaceTimelineClip> hideHudClips)
{
    public string Name { get; } = name;
    public UnityExportMetadata Metadata { get; } = metadata;
    public TimelineStructureDocument Structure { get; } = structure;
    public IReadOnlyList<KaraokeClip> KaraokeClips { get; } = karaokeClips;
    public IReadOnlyList<PictogramEntry> PictogramClips { get; } = pictogramClips;
    public IReadOnlyList<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> MotionClips { get; } = motionClips;
    public IReadOnlyList<GoldEffectTimelineClip> GoldEffectClips { get; } = goldEffectClips;
    public IReadOnlyList<HideUserInterfaceTimelineClip> HideHudClips { get; } = hideHudClips;
}

// TODO: is this needed? Can't we just use UnitySongInfo?
public sealed class UnityExportMetadata
{
    [JsonPropertyName("songID")]
    public required Guid SongId { get; init; }

    [JsonPropertyName("artist")]
    public required string Artist { get; init; }

    [JsonPropertyName("coachCount")]
    public required int CoachCount { get; init; }

    [JsonPropertyName("coachNamesLocIds")]
    public required IReadOnlyList<string> CoachNamesLocIds { get; init; }

    [JsonPropertyName("credits")]
    public required string Credits { get; init; }

    [JsonPropertyName("danceVersionLocId")]
    public required int DanceVersionLocId { get; init; }

    [JsonPropertyName("difficulty")]
    public required uint Difficulty { get; init; }

    [JsonPropertyName("doubleScoringType")]
    public string? DoubleScoringType { get; init; }

    [JsonPropertyName("hasSongTitleInCover")]
    public bool HasSongTitleInCover { get; init; }

    [JsonPropertyName("lyricsColor")]
    public required string LyricsColor { get; init; }

    [JsonPropertyName("mapLength")]
    public required double MapLength { get; init; }

    [JsonPropertyName("mapName")]
    public required string MapName { get; init; }

    [JsonPropertyName("originalJDVersion")]
    public required uint OriginalJdVersion { get; init; }

    [JsonPropertyName("parentMapName")]
    public required string ParentMapName { get; init; }

    [JsonPropertyName("sweatDifficulty")]
    public required uint SweatDifficulty { get; init; }

    [JsonPropertyName("tagIds")]
    public required IReadOnlyList<Guid> TagIds { get; init; }

    [JsonPropertyName("tags")]
    public required IReadOnlyList<string> Tags { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }
}