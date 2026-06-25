using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal abstract class LegacyResourceFileBinary<TComponent>
    where TComponent : LegacyResourceComponent
{
    public uint Version { get; set; }
    public uint SerializedSize { get; set; }
    public LegacyBinaryTypeId<LegacyResourceBaseBinary> BaseTypeId { get; set; }
    public uint BaseTypeSize { get; set; }

    [LegacyBinaryPadding(28)]
    public LegacyPadding Reserved { get; set; }

    public int ComponentCount { get; set; }
    public TComponent Component { get; set; } = null!;
}

internal abstract class LegacyResourceComponent
{
    public uint ComponentSize { get; set; }
}

internal sealed class LegacySongDesc : LegacyResourceFileBinary<LegacySongDescComponent>
{
    public static explicit operator SongDesc(LegacySongDesc value) =>
        (SongDesc)value.Component;
}

internal sealed class LegacyBlockFlow : LegacyResourceFileBinary<LegacyBlockFlowTemplateComponent>
{
    public LegacyMashupData ToMashupData(string mashupMapName, string baseSongName, bool hasDatabaseGameId)
    {
        LegacyBlockReplacement[] replacements = Component.BlockDescriptorVector;
        List<LegacyMashupBlock> blocks = new(replacements.Length);
        int absoluteStartBeat = 0;

        for (int i = 0; i < replacements.Length; i++)
        {
            LegacyBlockReplacement replacement = replacements[i];
            LegacyBlockDescriptor source = replacement.BaseBlock.IsEmptyBlock != 0
                ? replacement.BaseBlock
                : replacement.AlternativeBlocks.Length > 0
                ? replacement.AlternativeBlocks[0]
                : replacement.BaseBlock;

            LegacyMashupBlock block = new()
            {
                Index = i,
                AbsoluteStartBeat = absoluteStartBeat,
                BaseBlock = replacement.BaseBlock.ToRuntime(hasDatabaseGameId),
                SourceBlock = source.ToRuntime(hasDatabaseGameId),
                UsesAlternativeBlock = replacement.BaseBlock.IsEmptyBlock == 0 && replacement.AlternativeBlocks.Length > 0
            };
            blocks.Add(block);

            absoluteStartBeat += Math.Max(0, source.LastBeat - source.FirstBeat);
        }

        return new LegacyMashupData
        {
            MapName = mashupMapName,
            BaseSongName = baseSongName,
            Blocks = blocks
        };
    }
}

[LegacyBinaryTypeId(0x5B648E44)]
internal sealed class LegacyBlockFlowTemplateComponent : LegacyResourceComponent
{
    public int IsMashUp { get; set; }
    public int IsPartyMaster { get; set; }
    public LegacyBlockReplacement[] BlockDescriptorVector { get; set; } = [];
}

internal sealed class LegacyBlockReplacement
{
    public int SerializedSize { get; set; }
    public LegacyBlockDescriptor BaseBlock { get; set; } = new();
    public LegacyBlockDescriptor[] AlternativeBlocks { get; set; } = [];
}

internal sealed class LegacyBlockDescriptor
{
    public int SerializedSize { get; set; }
    public string SongName { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MaxEngineVersion = 2014)]
    public int DatabaseGameId { get; set; }

    public int FirstBeat { get; set; }
    public int LastBeat { get; set; }
    public int SongSwitch { get; set; }
    public float VideoCoachOffsetX { get; set; }
    public float VideoCoachOffsetY { get; set; }
    public float VideoCoachScale { get; set; }
    public string DanceStepName { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)]
    public float PlayingSpeed { get; set; }

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)]
    public int IsEntryPoint { get; set; }

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)]
    public int IsEmptyBlock { get; set; }

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)]
    public int IsNoScoreBlock { get; set; }

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)]
    public string Guid { get; set; } = string.Empty;

    public LegacyMashupBlockDescriptor ToRuntime(bool hasDatabaseGameId) => new()
    {
        SongName = SongName,
        DatabaseGameId = hasDatabaseGameId ? DatabaseGameId : null,
        FirstBeat = FirstBeat,
        LastBeat = LastBeat,
        SongSwitch = SongSwitch != 0,
        SongSwitchValue = SongSwitch,
        VideoCoachOffsetX = VideoCoachOffsetX,
        VideoCoachOffsetY = VideoCoachOffsetY,
        VideoCoachScale = VideoCoachScale == 0 ? 1.0f : VideoCoachScale,
        DanceStepName = DanceStepName,
        PlayingSpeed = PlayingSpeed == 0 ? 1.0f : PlayingSpeed,
        IsEntryPoint = IsEntryPoint != 0,
        IsEmptyBlock = IsEmptyBlock != 0,
        IsNoScoreBlock = IsNoScoreBlock != 0,
        Guid = Guid
    };
}

internal abstract class LegacySongDescComponent : LegacyResourceComponent
{
    public string MapName { get; set; } = string.Empty;
    public uint RawEngineVersion { get; set; }

    public static explicit operator SongDesc(LegacySongDescComponent value) =>
        value switch
        {
            LegacyJd2014SongDescComponent jd2014 => (SongDesc)jd2014,
            LegacyModernSongDescComponent classic => (SongDesc)classic,
            LegacyGeneratedModernSongDescComponent generated => (SongDesc)generated,
            _ => throw new InvalidDataException($"Unsupported legacy songdesc component '{value.GetType().Name}'.")
        };

}

[LegacyBinaryTypeId(0x8AC2B5C6, MaxEngineVersion = 2014)]
internal sealed class LegacyJd2014SongDescComponent : LegacySongDescComponent
{
    private const int BetaComponentSize = 0x88;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string[] RelatedAlbums { get; set; } = [];

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown0 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown1 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown2 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacyJd2014SongDescEntry[] SongDescEntries { get; set; } = [];

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public string BetaArtist { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public string BetaTitle { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string Artist { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string Title { get; set; } = string.Empty;

    public uint CoachCount { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaDifficulty { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaDefaultCoachId { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown3 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint DefaultCoachId { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint Difficulty { get; set; }

    public float TagScale { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacySongDescPreview Preview { get; set; } = new();

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint LyricColorIntensity { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyJd2014SongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                JDVersion = 2014,
                OriginalJDVersion = 2014,
                Artist = value.IsBeta ? value.BetaArtist : value.Artist,
                DancerName = string.Empty,
                Title = value.IsBeta ? value.BetaTitle : value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.EffectiveDefaultCoachId == uint.MaxValue ? 0 : checked((int)value.EffectiveDefaultCoachId),
                Difficulty = value.EffectiveDifficulty,
                SweatDifficulty = 0,
                Status = 0,
                LocaleID = 1,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.IsBeta ? [1f, 1f, 1f, 1f] : value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };

    private bool IsBeta => ComponentSize == BetaComponentSize;
    private uint EffectiveDefaultCoachId => IsBeta ? BetaDefaultCoachId : DefaultCoachId;
    private uint EffectiveDifficulty => IsBeta ? BetaDifficulty : Difficulty;
}

[LegacyBinaryTypeId(0x8AC2B5C6, MinEngineVersion = 2015, MaxEngineVersion = 2015)]
internal sealed class LegacyModernSongDescComponent : LegacySongDescComponent
{
    public uint OriginalVersion { get; set; }
    public int HasBaseMap { get; set; }

    [LegacyBinaryCondition(nameof(HasBaseMap), 1)]
    public string? BaseMapName { get; set; }

    public LegacySongDescTags Tags { get; set; } = new();
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public uint CoachCount { get; set; }
    public uint DefaultCoachId { get; set; }
    public uint Difficulty { get; set; }
    public int SweatDifficulty { get; set; }
    public int Status { get; set; }
    public int LocaleId { get; set; }
    public float TagScale { get; set; }
    [LegacyBinaryCondition(nameof(HasBaseMap), 1, Invert = true)]
    public LegacySongDescPreview? Preview { get; set; } = new();

    [LegacyBinaryCondition(nameof(HasBaseMap), 1)]
    public LegacySongDescEmptyPreview? EmptyPreview { get; set; }

    public uint LyricColorIntensity { get; set; }
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyModernSongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                JDVersion = value.RawEngineVersion,
                OriginalJDVersion = value.OriginalVersion,
                Artist = value.Artist,
                DancerName = value.DancerName,
                Title = value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.DefaultCoachId == uint.MaxValue ? 0 : checked((int)value.DefaultCoachId),
                Difficulty = value.Difficulty,
                SweatDifficulty = value.SweatDifficulty < 0 ? 0u : (uint)value.SweatDifficulty,
                Status = value.Status,
                LocaleID = value.LocaleId,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };
}

[LegacyBinaryTypeId(0x8AC2B5C6, MinEngineVersion = 2016)]
internal sealed class LegacyGeneratedModernSongDescComponent : LegacySongDescComponent
{
    public uint OriginalVersion { get; set; }
    public int HasBaseMap { get; set; }

    [LegacyBinaryCondition(nameof(HasBaseMap), 1)]
    public string? BaseMapName { get; set; }

    public LegacyFixedSongDescTags Tags { get; set; } = new();
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public uint CoachCount { get; set; }
    public uint DefaultCoachId { get; set; }
    public uint Difficulty { get; set; }
    public int SweatDifficulty { get; set; }
    public int Status { get; set; }
    public int LocaleId { get; set; }
    public float TagScale { get; set; }
    public LegacySongDescPreview Preview { get; set; } = new();
    public LegacySongDescVersionBlock VersionBlock { get; set; } = new();
    public float LyricColorIntensity { get; set; }
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyGeneratedModernSongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                JDVersion = value.RawEngineVersion,
                OriginalJDVersion = value.OriginalVersion,
                Artist = value.Artist,
                DancerName = value.DancerName,
                Title = value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.DefaultCoachId == uint.MaxValue ? 0 : checked((int)value.DefaultCoachId),
                Difficulty = value.Difficulty,
                SweatDifficulty = value.SweatDifficulty < 0 ? 0u : (uint)value.SweatDifficulty,
                Status = value.Status,
                LocaleID = value.LocaleId,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };
}

internal sealed class LegacySongDescTags
{
    public LegacySongDescTagEntry[] Entries { get; set; } = [];
}

internal sealed class LegacySongDescTagEntry
{
    public int SerializedSize { get; set; }
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int Unknown2 { get; set; }
    public uint Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public int Unknown6 { get; set; }
    public int Unknown7 { get; set; }
    public int Unknown8 { get; set; }
    public uint Unknown9 { get; set; }
    public int Unknown10 { get; set; }
    public int Unknown11 { get; set; }
}

internal sealed class LegacyFixedSongDescTags
{
    public int Version { get; set; }
    public int SerializedSize { get; set; }
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int TagCount { get; set; }
    public uint DefaultTag { get; set; }
    public int LocaleCount { get; set; }
    public int Unknown2 { get; set; }
    public int Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public uint DefaultLocale { get; set; }
    public int Unknown6 { get; set; }
    public int Enabled { get; set; }
}

internal sealed class LegacyJd2014SongDescEntry
{
    public uint SerializedSize { get; set; }
    public uint Group { get; set; }
    public uint Value { get; set; }
    public uint Category { get; set; }
    public uint Unknown0 { get; set; }
    public uint HasInlineColor { get; set; }

    [LegacyBinaryCondition(nameof(HasInlineColor), 1)]
    public LegacyTypedAbgrColor? InlineColor { get; set; }

    public uint Unknown1 { get; set; }
    public uint Unknown2 { get; set; }
    public uint InvalidValue { get; set; }
    public uint Unknown3 { get; set; }
    public uint Enabled { get; set; }
}

internal sealed class LegacySongDescPreview
{
    public int PreviewCount { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntrySize { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public uint PreviewEntryTypeId { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntryBeat { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntryPadding { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopSize { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public uint PreviewLoopTypeId { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopStartBeat { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopEndBeat { get; set; }
}

internal sealed class LegacySongDescEmptyPreview
{
    public int PreviewCount { get; set; }
}

internal sealed class LegacySongDescVersionBlock
{
    public int Version { get; set; }
    public uint TypeId { get; set; }
    public float Unknown0 { get; set; }
    public float Unknown1 { get; set; }
    public float Unknown2 { get; set; }
}

internal sealed class LegacyTypedAbgrColor
{
    public LegacyBinaryTypeId<LegacyColorBinary> TypeId { get; set; }
    public LegacyAbgrColorValue Color { get; set; } = new();
}

internal sealed class LegacyAbgrColorValue
{
    public float Alpha { get; set; }
    public float Blue { get; set; }
    public float Green { get; set; }
    public float Red { get; set; }

    public float[] ToRgba() => [Alpha, Red, Green, Blue];
}

internal sealed class LegacyMusicTrack : LegacyResourceFileBinary<LegacyMusicTrackComponent>
{
    public static explicit operator MusicTrack(LegacyMusicTrack value) =>
        (MusicTrack)value.Component;
}

internal abstract class LegacyMusicTrackComponent : LegacyResourceComponent
{
    public uint StructureSize { get; set; }
    public uint MarkerListSize { get; set; }
    public int[] Markers { get; set; } = [];
    public LegacyMusicSignatureMarker[] Signatures { get; set; } = [];
    public LegacyMusicSectionMarker[] Sections { get; set; } = [];
    public int StartBeat { get; set; }
    public uint EndBeat { get; set; }

    public abstract float VideoStartTime { get; }
    public abstract string RuntimeAudioPath { get; }

    public static explicit operator MusicTrack(LegacyMusicTrackComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new TrackDataHolder
            {
                Class = "MusicTrackComponent_Template",
                TrackData = new TrackData
                {
                    Class = "MusicTrackData",
                    Path = value.RuntimeAudioPath,
                    Structure = new Structure
                    {
                        StartBeat = value.StartBeat,
                        EndBeat = checked((int)value.EndBeat),
                        VideoStartTime = value.VideoStartTime,
                        Markers = [.. value.Markers],
                        Signatures = [.. value.Signatures.Select(signature => (Signature)signature)],
                        Sections = [.. value.Sections.Select(section => (Section)section)]
                    }
                }
            }
        ]
    };
}

[LegacyBinaryTypeId(0x02883A7E, MaxEngineVersion = 2014)]
internal sealed class LegacyJd2014MusicTrackComponent : LegacyMusicTrackComponent
{
    public float VideoStartTimeValue { get; set; }
    public LegacyUbiArtFolderFirstPath AudioPath { get; set; }

    [LegacyBinaryPadding(8)]
    public LegacyPadding Footer { get; set; }

    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

[LegacyBinaryTypeId(0x02883A7E, MinEngineVersion = 2015, MaxEngineVersion = 2017)]
internal sealed class LegacyClassicMusicTrackComponent : LegacyMusicTrackComponent
{
    public float VideoStartTimeValue { get; set; }

    [LegacyBinaryPadding(4)]
    public LegacyPadding FooterPadding { get; set; }

    public LegacyUbiArtPath AudioPath { get; set; }

    [LegacyBinaryPadding(8)]
    public LegacyPadding Footer { get; set; }

    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

[LegacyBinaryTypeId(0x02883A7E, MinEngineVersion = 2018)]
internal sealed class LegacyModernMusicTrackComponent : LegacyMusicTrackComponent
{
    [LegacyBinaryPadding(10)]
    public LegacyPadding ModernPadding { get; set; }

    public float VideoStartTimeValue { get; set; }

    [LegacyBinaryPadding(20)]
    public LegacyPadding FooterPadding { get; set; }

    public LegacyUbiArtPath AudioPath { get; set; }

    [LegacyBinaryPadding(8)]
    public LegacyPadding Footer { get; set; }

    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

internal sealed class LegacyMusicSignatureMarker
{
    public int Size { get; set; }
    public int Marker { get; set; }
    public int Beats { get; set; }

    public static explicit operator Signature(LegacyMusicSignatureMarker value) => new()
    {
        Marker = value.Marker,
        Beats = value.Beats
    };
}

internal sealed class LegacyMusicSectionMarker
{
    public int Size { get; set; }
    public int StartBeat { get; set; }
    public int SectionType { get; set; }
    public string Comment { get; set; } = string.Empty;

    public static explicit operator Section(LegacyMusicSectionMarker value) => new()
    {
        Marker = value.StartBeat,
        SectionType = value.SectionType,
        Comment = value.Comment
    };
}

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

    public static explicit operator Clip(LegacyGameplayClip value) =>
        value switch
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

    [LegacyBinaryPadding(64)]
    public LegacyPadding PlatformSpecifics { get; set; }

    public static explicit operator MotionClip(LegacyMotionGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        ClassifierPath = value.ClassifierPath.FullPath,
        GoldMove = value.GoldMove,
        CoachId = value.CoachId,
        MoveType = value.MoveType,
        Color = value.Color.ToRgba()
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
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        PictoPath = value.PictoPath.FullPath,
        CoachCount = unchecked((int)value.CoachCount)
    };
}

[LegacyBinaryTypeId(0xFD69B110)]
internal sealed class LegacyGoldEffectGameplayClip : LegacyGameplayClip
{
    public int EffectType { get; set; }

    public static explicit operator GoldEffectClip(LegacyGoldEffectGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        EffectType = value.EffectType
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
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        Pitch = value.Pitch,
        Lyrics = value.Lyrics,
        IsEndOfLine = value.IsEndOfLine,
        ContentType = value.ContentType,
        StartTimeTolerance = value.StartTimeTolerance,
        EndTimeTolerance = value.EndTimeTolerance,
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
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        SoundSetPath = value.SoundSetPath.FullPath,
        SoundChannel = value.SoundChannel,
        StopsOnEnd = value.StopsOnEnd,
        AccountedForDuration = value.AccountedForDuration,
        StartOffset = value.StartOffset
    };
}

[LegacyBinaryTypeId(0x52E06A9A, MaxEngineVersion = 2015)]
internal sealed class LegacyJd2015HideUserInterfaceGameplayClip : LegacyGameplayClip
{
    [LegacyBinaryPadding(4)]
    public LegacyPadding VersionPadding { get; set; }

    public int EventType { get; set; }
    public int Padding { get; set; }

    public static explicit operator HideUserInterfaceClip(LegacyJd2015HideUserInterfaceGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        EventType = value.EventType
    };
}

[LegacyBinaryTypeId(0x52E06A9A, MinEngineVersion = 2016)]
internal sealed class LegacyHideUserInterfaceGameplayClip : LegacyGameplayClip
{
    [LegacyBinaryPadding(8)]
    public LegacyPadding VersionPadding { get; set; }

    public int EventType { get; set; }
    public int Padding { get; set; }

    public static explicit operator HideUserInterfaceClip(LegacyHideUserInterfaceGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        EventType = value.EventType
    };
}

[LegacyBinaryTypeId(0x101F9D2B)]
internal sealed class LegacyVibrationGameplayClip : LegacyGameplayClip
{
    public static explicit operator VibrationClip(LegacyVibrationGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        VibrationFilePath = "world/_common/hd_rumble/bigpulse_01.vib",
        PlayerId = -1,
        Modulation = 0.5f
    };
}

[LegacyBinaryTypeId(0x0E1E8158)]
internal sealed class LegacyTapeReferenceGameplayClip : LegacyGameplayClip
{
    public LegacyUbiArtPath Path { get; set; }
    public int Loop { get; set; }

    [LegacyBinaryPadding(8)]
    public LegacyPadding Padding { get; set; }

    public static explicit operator TapeReferenceClip(LegacyTapeReferenceGameplayClip value) => new()
    {
        Id = value.Id,
        TrackId = value.TrackId,
        IsActive = value.IsActive,
        StartTime = value.StartTime,
        Duration = value.Duration,
        Path = value.Path.FullPath,
        Loop = value.Loop
    };
}

internal static class LegacyGameplayBinaryHelpers
{
    public static int RoundBeatsToFrames(float value) =>
        checked((int)Math.Round(value * 24f, MidpointRounding.ToEven));
}