using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal sealed class LegacyClipTape
{
    public uint Version { get; set; }
    public uint SerializedSize { get; set; }
    public LegacyBinaryTypeId<LegacyClipTapeBinary> TypeId { get; set; }
    public uint TypeSize { get; set; }
    public LegacyGameplayClip[] Clips { get; set; } = [];
    public static explicit operator ClipTape(LegacyClipTape value) => new()
    {
        Clips = [.. value.Clips.Select(clip => (Clip)clip)]
    };
}

internal abstract class LegacyGameplayClip
{
    public int SerializedSize { get; set; }
    public uint Id { get; set; }
    public uint TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public static explicit operator Clip(LegacyGameplayClip value) => value switch
    {
        LegacyMotionGameplayClip motion => (MotionClip)motion,
        LegacyPictogramGameplayClip pictogram => (PictogramClip)pictogram,
        LegacyGoldEffectGameplayClip gold => (GoldEffectClip)gold,
        LegacyKaraokeGameplayClip karaoke => (KaraokeClip)karaoke,
        LegacySoundSetGameplayClip soundSet => (SoundSetClip)soundSet,
        LegacyJd2015HideUserInterfaceGameplayClip jd2015HideHud => (HideUserInterfaceClip)jd2015HideHud,
        LegacyHideUserInterfaceGameplayClip hideHud => (HideUserInterfaceClip)hideHud,
        LegacyVibrationGameplayClip vibration => (VibrationClip)vibration,
        LegacyTapeReferenceGameplayClip tapeReference => (TapeReferenceClip)tapeReference,
        _ => throw new InvalidDataException($"Unsupported legacy gameplay clip '{value.GetType().Name}'.")
    };
}

[LegacyBinaryTypeId(0x955384A1)]
internal sealed class LegacyMotionGameplayClip : LegacyGameplayClip
{
    public LegacyUbiArtPath ClassifierPath { get; set; }
    public int Unknown0 { get; set; }
    public int GoldMove { get; set; }
    public int CoachId { get; set; }
    public int MoveType { get; set; }
    public LegacyAbgrColorValue Color { get; set; } = new();
    [LegacyBinaryPadding(64)] public LegacyPadding PlatformSpecifics { get; set; }
    public static explicit operator MotionClip(LegacyMotionGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration,
        ClassifierPath = value.ClassifierPath.FullPath, GoldMove = value.GoldMove,
        CoachId = value.CoachId, MoveType = value.MoveType, Color = value.Color.ToRgba()
    };
}

[LegacyBinaryTypeId(0x52EC8962)]
internal sealed class LegacyPictogramGameplayClip : LegacyGameplayClip
{
    public LegacyUbiArtPath PictoPath { get; set; }
    public int Unknown0 { get; set; }
    public uint CoachCount { get; set; }
    public static explicit operator PictogramClip(LegacyPictogramGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration,
        PictoPath = value.PictoPath.FullPath, CoachCount = unchecked((int)value.CoachCount)
    };
}

[LegacyBinaryTypeId(0xFD69B110)]
internal sealed class LegacyGoldEffectGameplayClip : LegacyGameplayClip
{
    public int EffectType { get; set; }
    public static explicit operator GoldEffectClip(LegacyGoldEffectGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration, EffectType = value.EffectType
    };
}

[LegacyBinaryTypeId(0x68552A41)]
internal sealed class LegacyKaraokeGameplayClip : LegacyGameplayClip
{
    public float Pitch { get; set; }
    public string Lyrics { get; set; } = string.Empty;
    public int IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public float SemitoneTolerance { get; set; }
    public static explicit operator KaraokeClip(LegacyKaraokeGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration, Pitch = value.Pitch,
        Lyrics = value.Lyrics, IsEndOfLine = value.IsEndOfLine, ContentType = value.ContentType,
        StartTimeTolerance = value.StartTimeTolerance, EndTimeTolerance = value.EndTimeTolerance,
        SemitoneTolerance = value.SemitoneTolerance
    };
}

[LegacyBinaryTypeId(0x2D8C885B)]
internal sealed class LegacySoundSetGameplayClip : LegacyGameplayClip
{
    public LegacyUbiArtPath SoundSetPath { get; set; }
    public int SoundChannel { get; set; }
    public int StopsOnEnd { get; set; }
    public int AccountedForDuration { get; set; }
    public int StartOffset { get; set; }
    public static explicit operator SoundSetClip(LegacySoundSetGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration, SoundSetPath = value.SoundSetPath.FullPath,
        SoundChannel = value.SoundChannel, StopsOnEnd = value.StopsOnEnd,
        AccountedForDuration = value.AccountedForDuration, StartOffset = value.StartOffset
    };
}

[LegacyBinaryTypeId(0x52E06A9A, MaxEngineVersion = 2015)]
internal sealed class LegacyJd2015HideUserInterfaceGameplayClip : LegacyGameplayClip
{
    [LegacyBinaryPadding(4)] public LegacyPadding VersionPadding { get; set; }
    public int EventType { get; set; }
    public int Padding { get; set; }
    public static explicit operator HideUserInterfaceClip(LegacyJd2015HideUserInterfaceGameplayClip value) =>
        LegacyGameplayClipConversions.ToHideUserInterface(value, value.EventType);
}

[LegacyBinaryTypeId(0x52E06A9A, MinEngineVersion = 2016)]
internal sealed class LegacyHideUserInterfaceGameplayClip : LegacyGameplayClip
{
    [LegacyBinaryPadding(8)] public LegacyPadding VersionPadding { get; set; }
    public int EventType { get; set; }
    public int Padding { get; set; }
    public static explicit operator HideUserInterfaceClip(LegacyHideUserInterfaceGameplayClip value) =>
        LegacyGameplayClipConversions.ToHideUserInterface(value, value.EventType);
}

[LegacyBinaryTypeId(0x101F9D2B)]
internal sealed class LegacyVibrationGameplayClip : LegacyGameplayClip
{
    public static explicit operator VibrationClip(LegacyVibrationGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration,
        VibrationFilePath = "world/_common/hd_rumble/bigpulse_01.vib", PlayerId = -1, Modulation = 0.5f
    };
}

[LegacyBinaryTypeId(0x0E1E8158)]
internal sealed class LegacyTapeReferenceGameplayClip : LegacyGameplayClip
{
    public LegacyUbiArtPath Path { get; set; }
    public int Loop { get; set; }
    [LegacyBinaryPadding(8)] public LegacyPadding Padding { get; set; }
    public static explicit operator TapeReferenceClip(LegacyTapeReferenceGameplayClip value) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration, Path = value.Path.FullPath, Loop = value.Loop
    };
}

internal static class LegacyGameplayClipConversions
{
    public static HideUserInterfaceClip ToHideUserInterface(LegacyGameplayClip value, int eventType) => new()
    {
        Id = value.Id, TrackId = value.TrackId, IsActive = value.IsActive,
        StartTime = value.StartTime, Duration = value.Duration, EventType = eventType
    };
}

internal static class LegacyGameplayBinaryHelpers
{
    public static int RoundBeatsToFrames(float value) =>
        checked((int)Math.Round(value * 24f, MidpointRounding.ToEven));
}
