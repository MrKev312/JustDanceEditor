using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Model;

internal sealed class LegacyJd2014Timeline
{
    public uint Version { get; set; }
    public uint SerializedSize { get; set; }
    public LegacyBinaryTypeId<LegacyResourceBaseBinary> BaseTypeId { get; set; }
    public uint BaseTypeSize { get; set; }

    [LegacyBinaryPadding(28)]
    public LegacyPadding Reserved { get; set; }

    public int ComponentCount { get; set; }
    public LegacyBinaryTypeId<LegacyJd2014TimelineComponentBinary> ComponentTypeId { get; set; }
    public uint ComponentSize { get; set; }
    public string MapName { get; set; } = string.Empty;
    public LegacyJd2014TimelineDefaults Defaults { get; set; } = new();
    public LegacyJd2014TimelineIgnoredEntry[] TimelineEntries { get; set; } = [];
    public LegacyJd2014PictogramEntry[] Pictograms { get; set; } = [];
    public LegacyJd2014MotionEntry[] Moves { get; set; } = [];
    public LegacyJd2014MotionEntry[] Gestures { get; set; } = [];
    public LegacyJd2014KaraokeEntry[] Lyrics { get; set; } = [];
    public LegacyJd2014TimelineEvent[] Events { get; set; } = [];

    [BinarySerializerIgnore]
    public ClipTape DanceTape => new()
    {
        Clips = [.. BuildDanceClips()]
    };

    [BinarySerializerIgnore]
    public ClipTape KaraokeTape => new()
    {
        Clips = [.. BuildKaraokeClips()]
    };

    private IEnumerable<Clip> BuildDanceClips()
    {
        uint clipId = 1;

        foreach (LegacyJd2014PictogramEntry entry in Pictograms)
        {
            PictogramClip clip = (PictogramClip)entry;
            clip.Id = clipId++;
            yield return clip;
        }

        foreach (LegacyJd2014MotionEntry entry in Moves)
        {
            MotionClip clip = (MotionClip)entry;
            clip.Id = clipId++;
            clip.MoveType = 0;
            yield return clip;
        }

        foreach (LegacyJd2014MotionEntry entry in Gestures)
        {
            MotionClip clip = (MotionClip)entry;
            clip.Id = clipId++;
            clip.MoveType = 1;
            yield return clip;
        }

        foreach (LegacyJd2014TimelineEvent timelineEvent in Events)
        {
            foreach (Clip clip in (Clip[])timelineEvent)
            {
                clip.Id = clipId++;
                yield return clip;
            }
        }
    }

    private IEnumerable<Clip> BuildKaraokeClips()
    {
        uint clipId = 1;
        foreach (LegacyJd2014KaraokeEntry entry in Lyrics)
        {
            KaraokeClip clip = (KaraokeClip)entry;
            clip.Id = clipId++;
            yield return clip;
        }
    }
}

internal sealed class LegacyJd2014TimelineDefaults
{
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int Unknown2 { get; set; }
    public int Unknown3 { get; set; }
    public uint Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public int Unknown6 { get; set; }
    public int Unknown7 { get; set; }
    public int Unknown8 { get; set; }
    public float Unknown9 { get; set; }
    public float Unknown10 { get; set; }
    public float Unknown11 { get; set; }
    public float Unknown12 { get; set; }
    public float Unknown13 { get; set; }
    public float Unknown14 { get; set; }
    public int Unknown15 { get; set; }
    public int Unknown16 { get; set; }
    public float Unknown17 { get; set; }
    public int Unknown18 { get; set; }
}

internal sealed class LegacyJd2014TimelineIgnoredEntry
{
    public uint Unknown0 { get; set; }
    public uint Unknown1 { get; set; }
    public uint Unknown2 { get; set; }
    public uint Unknown3 { get; set; }
    public uint Unknown4 { get; set; }
    public uint Unknown5 { get; set; }
    public uint Unknown6 { get; set; }
    public uint Unknown7 { get; set; }
    public uint Unknown8 { get; set; }
    public uint Unknown9 { get; set; }
    public uint Unknown10 { get; set; }
}

internal sealed class LegacyJd2014PictogramEntry
{
    public uint ClassType { get; set; }
    public float StartBeat { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public LegacyUbiArtFolderFirstPath Path { get; set; }
    public uint Padding { get; set; }

    public static explicit operator PictogramClip(LegacyJd2014PictogramEntry value) => new()
    {
        TrackId = 1,
        IsActive = value.IsActive > 0 ? 1 : 0,
        StartTime = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.StartBeat),
        Duration = 24,
        PictoPath = value.Path.FullPath,
        CoachCount = uint.MaxValue
    };
}

internal sealed class LegacyJd2014MotionEntry
{
    public uint ClassType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public uint CoachId { get; set; }
    public LegacyUbiArtFolderFirstPath Path { get; set; }
    public uint Padding { get; set; }
    public float StartBeat { get; set; }
    public float EndBeat { get; set; }
    public int GoldMove { get; set; }
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int Unknown2 { get; set; }

    public static explicit operator MotionClip(LegacyJd2014MotionEntry value)
    {
        int startTime = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.StartBeat);
        return new MotionClip
        {
            TrackId = 1,
            IsActive = 1,
            StartTime = startTime,
            Duration = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.EndBeat) - startTime,
            ClassifierPath = value.Path.FullPath,
            GoldMove = value.GoldMove,
            CoachId = checked((int)value.CoachId),
            Color = [1.0f, 0.988235f, 0.592157f, 0.572549f]
        };
    }
}

internal sealed class LegacyJd2014KaraokeEntry
{
    public uint ClassType { get; set; }
    public string Lyrics { get; set; } = string.Empty;
    public uint Flags { get; set; }
    public int IsEndOfLine { get; set; }
    public float StartBeat { get; set; }
    public float EndBeat { get; set; }

    public static explicit operator KaraokeClip(LegacyJd2014KaraokeEntry value)
    {
        int startTime = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.StartBeat);
        return new KaraokeClip
        {
            TrackId = 0,
            IsActive = 1,
            StartTime = startTime,
            Duration = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.EndBeat) - startTime,
            Pitch = 8.661958f,
            Lyrics = value.Lyrics,
            IsEndOfLine = value.IsEndOfLine,
            ContentType = 1,
            StartTimeTolerance = 4,
            EndTimeTolerance = 4,
            SemitoneTolerance = 5
        };
    }
}

internal sealed class LegacyJd2014TimelineEvent
{
    public uint SerializedSizeOrMarker { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0)]
    public uint GeneratedExtraSize { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0)]
    [LegacyBinaryCondition(nameof(GeneratedExtraSize), 0)]
    public LegacyJd2014GeneratedTimelineEventBody? GeneratedBody { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0)]
    [LegacyBinaryCondition(nameof(GeneratedExtraSize), 0, Invert = true)]
    [LegacyBinaryByteCount(nameof(GeneratedExtraSize), Add = 86)]
    public byte[]? GeneratedExtraPayload { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public string? SourceId { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public float SourceStartBeat { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public float SourceEndBeat { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public int SourceLayerId { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public string? SourceModelName { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public uint SourceColor { get; set; }

    [LegacyBinaryCondition(nameof(SerializedSizeOrMarker), 0, Invert = true)]
    public LegacyJd2014BlockParameter[]? SourceParameters { get; set; }

    public static explicit operator Clip[](LegacyJd2014TimelineEvent value)
    {
        if (value.GeneratedBody != null)
            return (Clip[])value.GeneratedBody;

        if (!string.Equals(value.SourceModelName, "goldmove", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value.SourceModelName, "goldmovecascade", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        int startTime = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.SourceStartBeat);
        return
        [
            new GoldEffectClip
            {
                TrackId = 1,
                IsActive = 1,
                StartTime = startTime,
                Duration = LegacyGameplayBinaryHelpers.RoundBeatsToFrames(value.SourceEndBeat) - startTime,
                EffectType = 1
            }
        ];
    }
}

internal sealed class LegacyJd2014GeneratedTimelineEventBody
{
    public float StartBeat { get; set; }
    public float EndBeat { get; set; }
    public uint Flags { get; set; }
    public string EventName { get; set; } = string.Empty;

    [LegacyBinarySwitch(nameof(EventName))]
    public LegacyJd2014TimelineEventPayload Payload { get; set; } = null!;

    public int StartTime => LegacyGameplayBinaryHelpers.RoundBeatsToFrames(StartBeat);
    public int Duration => LegacyGameplayBinaryHelpers.RoundBeatsToFrames(EndBeat) - StartTime;

    public static explicit operator Clip[](LegacyJd2014GeneratedTimelineEventBody value) =>
        value.Payload.CreateClips(value.StartTime, value.Duration);
}

internal abstract class LegacyJd2014BlockParameter
{
    public uint SerializedSize { get; set; }
}

[LegacyBinaryTypeId(0x3DF448C5)]
internal sealed class LegacyJd2014BlockParameterInt32 : LegacyJd2014BlockParameter
{
    public int Value { get; set; }
}

[LegacyBinaryTypeId(0x6937C49F)]
internal sealed class LegacyJd2014BlockParameterUInt32 : LegacyJd2014BlockParameter
{
    public uint Value { get; set; }
}

[LegacyBinaryTypeId(0xF2655776)]
internal sealed class LegacyJd2014BlockParameterBool : LegacyJd2014BlockParameter
{
    public byte Value { get; set; }
}

[LegacyBinaryTypeId(0x572DE321)]
internal sealed class LegacyJd2014BlockParameterFloat : LegacyJd2014BlockParameter
{
    public float Value { get; set; }
}

[LegacyBinaryTypeId(0x458FE7FE)]
internal sealed class LegacyJd2014BlockParameterBeatFlag : LegacyJd2014BlockParameter
{
    public int Low { get; set; }
    public int Medium { get; set; }
    public int High { get; set; }
}

[LegacyBinaryTypeId(0x49695869)]
internal sealed class LegacyJd2014BlockParameterString : LegacyJd2014BlockParameter
{
    public string Value { get; set; } = string.Empty;
}

[LegacyBinaryTypeId(0x394B9227)]
internal sealed class LegacyJd2014BlockParameterFlag : LegacyJd2014BlockParameter
{
    public uint Value { get; set; }
    public int FlagType { get; set; }
}

[LegacyBinaryTypeId(0x09C7145E)]
internal sealed class LegacyJd2014BlockParameterEnum : LegacyJd2014BlockParameter
{
    public uint Value { get; set; }
    public int EnumType { get; set; }
}

internal abstract class LegacyJd2014TimelineEventPayload
{
    public virtual Clip[] CreateClips(int startTime, int duration) => [];
}

[LegacyBinarySwitchCase("eventdelayeve")]
internal sealed class LegacyJd2014EventDelayPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(197)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("tag")]
internal sealed class LegacyJd2014TagPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(67)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("eventmultieve")]
internal sealed class LegacyJd2014EventMultiPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(107)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("playsnd")]
internal sealed class LegacyJd2014PlaySoundPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(39)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("event_fadingmaterial")]
internal sealed class LegacyJd2014FadingMaterialPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(370)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("bpm")]
internal sealed class LegacyJd2014BpmPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(20)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("karaokescoring")]
internal sealed class LegacyJd2014KaraokeScoringPayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(8)]
    public LegacyPadding Payload { get; set; }
}

[LegacyBinarySwitchCase("goldmove")]
internal sealed class LegacyJd2014GoldMovePayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(16)]
    public LegacyPadding Payload { get; set; }

    public string MoveName { get; set; } = string.Empty;

    public override Clip[] CreateClips(int startTime, int duration) =>
    [
        new GoldEffectClip
        {
            TrackId = 1,
            IsActive = 1,
            StartTime = startTime,
            Duration = duration,
            EffectType = 1
        }
    ];
}

[LegacyBinarySwitchCase("goldmovecascade")]
internal sealed class LegacyJd2014GoldMoveCascadePayload : LegacyJd2014TimelineEventPayload
{
    [LegacyBinaryPadding(4)]
    public LegacyPadding Payload { get; set; }

    public string MoveName { get; set; } = string.Empty;

    public override Clip[] CreateClips(int startTime, int duration) =>
    [
        new GoldEffectClip
        {
            TrackId = 1,
            IsActive = 1,
            StartTime = startTime,
            Duration = duration,
            EffectType = 1
        }
    ];
}