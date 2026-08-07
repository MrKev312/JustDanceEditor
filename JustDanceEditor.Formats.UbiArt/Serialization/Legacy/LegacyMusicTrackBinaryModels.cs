using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal sealed class LegacyMusicTrack : LegacyResourceFileBinary<LegacyMusicTrackComponent>
{
    public static explicit operator MusicTrack(LegacyMusicTrack value) => (MusicTrack)value.Component;
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
        Components = [new TrackDataHolder
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
        }]
    };
}

[LegacyBinaryTypeId(0x02883A7E, MaxEngineVersion = 2014)]
internal sealed class LegacyJd2014MusicTrackComponent : LegacyMusicTrackComponent
{
    public float VideoStartTimeValue { get; set; }
    public LegacyUbiArtFolderFirstPath AudioPath { get; set; }
    [LegacyBinaryPadding(8)] public LegacyPadding Footer { get; set; }
    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

[LegacyBinaryTypeId(0x02883A7E, MinEngineVersion = 2015, MaxEngineVersion = 2017)]
internal sealed class LegacyClassicMusicTrackComponent : LegacyMusicTrackComponent
{
    public float VideoStartTimeValue { get; set; }
    [LegacyBinaryPadding(4)] public LegacyPadding FooterPadding { get; set; }
    public LegacyUbiArtPath AudioPath { get; set; }
    [LegacyBinaryPadding(8)] public LegacyPadding Footer { get; set; }
    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

[LegacyBinaryTypeId(0x02883A7E, MinEngineVersion = 2018)]
internal sealed class LegacyModernMusicTrackComponent : LegacyMusicTrackComponent
{
    [LegacyBinaryPadding(10)] public LegacyPadding ModernPadding { get; set; }
    public float VideoStartTimeValue { get; set; }
    [LegacyBinaryPadding(20)] public LegacyPadding FooterPadding { get; set; }
    public LegacyUbiArtPath AudioPath { get; set; }
    [LegacyBinaryPadding(8)] public LegacyPadding Footer { get; set; }
    public override float VideoStartTime => VideoStartTimeValue;
    public override string RuntimeAudioPath => AudioPath.FullPath;
}

internal sealed class LegacyMusicSignatureMarker
{
    public int Size { get; set; }
    public int Marker { get; set; }
    public int Beats { get; set; }
    public static explicit operator Signature(LegacyMusicSignatureMarker value) => new() { Marker = value.Marker, Beats = value.Beats };
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
