using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Unity.Models;

public sealed class UnityExportData
{
    public UnityExportData(
        string name,
        UnityExportMetadata metadata,
        UnityTrackStructure structure,
        IReadOnlyList<UnityKaraokeClip> karaokeClips,
        IReadOnlyList<UnityPictogramClip> pictogramClips,
        IReadOnlyList<UnityMotionClip> motionClips,
        IReadOnlyList<UnityGoldEffectClip> goldEffectClips,
        IReadOnlyList<UnityHideHudClip> hideHudClips,
        IReadOnlyList<UnityGameplayEventClip> gameplayEventClips,
        IReadOnlyList<UnityVibrationClip> vibrationClips)
    {
        Name = name;
        Metadata = metadata;
        Structure = structure;
        KaraokeClips = karaokeClips;
        PictogramClips = pictogramClips;
        MotionClips = motionClips;
        GoldEffectClips = goldEffectClips;
        HideHudClips = hideHudClips;
        GameplayEventClips = gameplayEventClips;
        VibrationClips = vibrationClips;

        List<UnityClip> combined =
        [
            .. KaraokeClips,
            .. PictogramClips,
            .. MotionClips,
            .. GoldEffectClips,
            .. HideHudClips,
            .. GameplayEventClips,
            .. VibrationClips,
        ];
        Clips = new ReadOnlyCollection<UnityClip>(combined);
    }

    public string Name { get; }
    public UnityExportMetadata Metadata { get; }
    public UnityTrackStructure Structure { get; }
    public IReadOnlyList<UnityKaraokeClip> KaraokeClips { get; }
    public IReadOnlyList<UnityPictogramClip> PictogramClips { get; }
    public IReadOnlyList<UnityMotionClip> MotionClips { get; }
    public IReadOnlyList<UnityGoldEffectClip> GoldEffectClips { get; }
    public IReadOnlyList<UnityHideHudClip> HideHudClips { get; }
    public IReadOnlyList<UnityGameplayEventClip> GameplayEventClips { get; }
    public IReadOnlyList<UnityVibrationClip> VibrationClips { get; }
    public IReadOnlyList<UnityClip> Clips { get; }
}

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

public sealed class UnityTrackStructure
{
    public int startBeat { get; init; }
    public int endBeat { get; init; }
    public double videoStartTime { get; init; }
    public double previewEntry { get; init; }
    public double previewLoopStart { get; init; }
    public double previewLoopEnd { get; init; }
    public int[] markers { get; init; } = [];
    public UnitySignature[] signatures { get; init; } = [];
    public UnitySection[] sections { get; init; } = [];
    public bool useFadeStartBeat { get; set; }
    public int fadeStartBeat { get; set; }
    public int fadeInType { get; set; }
    public bool useFadeEndBeat { get; set; }
    public int fadeEndBeat { get; set; }
    public int fadeOutType { get; set; }
}

public sealed class UnitySignature
{
    public float marker { get; init; }
    public int beats { get; init; }
}

public sealed class UnitySection
{
    public float marker { get; init; }
    public int sectionType { get; init; }
    public string comment { get; init; } = string.Empty;
}

public abstract class UnityClip
{
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
}

public sealed class UnityKaraokeClip : UnityClip
{
    public string Lyrics { get; set; } = string.Empty;
    public float Pitch { get; set; }
    public int IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public float SemitoneTolerance { get; set; }
}

public sealed class UnityPictogramClip : UnityClip
{
    public string PictoPath { get; set; } = string.Empty;
    public int CoachCount { get; set; }
}

public sealed class UnityMotionClip : UnityClip
{
    public string MoveName { get; set; } = string.Empty;
    public int GoldMove { get; set; }
    public int CoachId { get; set; }
    public int MoveType { get; set; }
}

public sealed class UnityGoldEffectClip : UnityClip
{
    public int EffectType { get; set; }
}

public sealed class UnityHideHudClip : UnityClip
{
}

public sealed class UnityGameplayEventClip : UnityClip
{
    public string Payload { get; set; } = string.Empty;
}

public sealed class UnityVibrationClip : UnityClip
{
}
