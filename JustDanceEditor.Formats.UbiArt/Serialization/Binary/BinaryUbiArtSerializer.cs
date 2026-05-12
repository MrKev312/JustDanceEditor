using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Binary;

public class BinaryUbiArtSerializer : IUbiArtSerializer
{
    private const uint ResourceBaseTypeId = 0x1B857BCE;
    private const uint SongDescComponentTypeId = 0x8AC2B5C6;
    private const uint MusicTrackComponentTypeId = 0x02883A7E;
    private const uint ClipTapeTypeId = 0x9E845460;

    private const uint MotionClipTypeId = 0x955384A1;
    private const uint PictogramClipTypeId = 0x52EC8962;
    private const uint GoldEffectClipTypeId = 0xFD69B110;
    private const uint KaraokeClipTypeId = 0x68552A41;
    private const uint SoundSetClipTypeId = 0x2D8C885B;
    private const uint HideUserInterfaceClipTypeId = 0x52E06A9A;
    private const uint VibrationClipTypeId = 0x101F9D2B;
    private const uint TapeReferenceClipTypeId = 0x0E1E8158;
    private const string DefaultLegacyVibrationPath = "world/_common/hd_rumble/bigpulse_01.vib";

    public T Deserialize<T>(Stream stream, JsonSerializerOptions? options = null) where T : new()
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (typeof(T) == typeof(SongDesc))
            return (T)(object)DeserializeSongDesc(stream);

        if (typeof(T) == typeof(MusicTrack))
            return (T)(object)DeserializeMusicTrack(stream);

        if (typeof(T) == typeof(ClipTape))
            return (T)(object)DeserializeClipTape(stream);

        throw new NotSupportedException($"Legacy binary deserialization for {typeof(T).Name} is not supported.");
    }

    private static SongDesc DeserializeSongDesc(Stream stream)
    {
        using BigEndianBinaryReader reader = new(stream);
        uint componentType = ReadResourceHeader(reader);
        if (componentType != SongDescComponentTypeId)
            throw new InvalidDataException($"Unexpected legacy SongDesc component type 0x{componentType:X8}.");

        string mapName = ReadString(reader);
        uint engineVersion = reader.ReadUInt32();
        uint originalVersion = reader.ReadUInt32();
        _ = reader.ReadInt32(); // Unknown0

        SkipSongDescTags(reader);

        string artist = ReadString(reader);
        string dancerName = ReadString(reader);
        string title = ReadString(reader);
        uint coachCount = reader.ReadUInt32();
        uint defaultCoachId = reader.ReadUInt32();
        uint difficulty = reader.ReadUInt32();
        int sweatDifficulty = reader.ReadInt32();
        int status = reader.ReadInt32();
        int localeId = reader.ReadInt32();
        _ = reader.ReadSingle(); // TagScale

        _ = reader.ReadInt32(); // PreviewCount
        _ = reader.ReadInt32(); // PreviewEntrySize
        _ = reader.ReadUInt32(); // PreviewEntryTypeId
        _ = reader.ReadInt32(); // PreviewEntryBeat
        _ = reader.ReadInt32(); // PreviewEntryPadding
        _ = reader.ReadInt32(); // PreviewLoopSize
        _ = reader.ReadUInt32(); // PreviewLoopTypeId
        _ = reader.ReadInt32(); // PreviewLoopStartBeat
        _ = reader.ReadInt32(); // PreviewLoopEndBeat

        if (engineVersion >= 2015)
            SkipBytes(reader, 20); // LegacySongDescVersionBlock

        _ = reader.ReadSingle(); // LyricColorIntensity
        _ = reader.ReadUInt32(); // LyricColorTypeId
        float[] lyricColor = ReadAbgrAsRgba(reader);

        return new SongDesc
        {
            Class = "Actor_Template",
            Components =
            [
                new InfoComponent
                {
                    Class = "JD_SongDescTemplate",
                    MapName = mapName,
                    JDVersion = engineVersion,
                    OriginalJDVersion = originalVersion,
                    Artist = artist,
                    DancerName = dancerName,
                    Title = title,
                    NumCoach = checked((int)coachCount),
                    MainCoach = defaultCoachId == uint.MaxValue ? 0 : checked((int)defaultCoachId),
                    Difficulty = difficulty,
                    SweatDifficulty = sweatDifficulty < 0 ? 0u : (uint)sweatDifficulty,
                    Status = status,
                    LocaleID = localeId,
                    DefaultColors = new DefaultColors
                    {
                        Lyrics = lyricColor
                    }
                }
            ]
        };
    }

    private static MusicTrack DeserializeMusicTrack(Stream stream)
    {
        using BigEndianBinaryReader reader = new(stream);
        uint componentType = ReadResourceHeader(reader);
        if (componentType != MusicTrackComponentTypeId)
            throw new InvalidDataException($"Unexpected legacy MusicTrack component type 0x{componentType:X8}.");

        _ = reader.ReadUInt32(); // StructureSize
        _ = reader.ReadUInt32(); // MarkerListSize

        int markerCount = reader.ReadInt32();
        int[] markers = ReadInt32Array(reader, markerCount);

        int signatureCount = reader.ReadInt32();
        Signature[] signatures = new Signature[signatureCount];
        for (int i = 0; i < signatures.Length; i++)
        {
            _ = reader.ReadInt32(); // Entry size
            int marker = reader.ReadInt32();
            int beats = reader.ReadInt32();
            signatures[i] = new Signature { Marker = marker, Beats = beats };
        }

        int sectionCount = reader.ReadInt32();
        Section[] sections = new Section[sectionCount];
        for (int i = 0; i < sections.Length; i++)
        {
            _ = reader.ReadInt32(); // Entry size
            int startBeat = reader.ReadInt32();
            int sectionType = reader.ReadInt32();
            string comment = ReadString(reader);
            sections[i] = new Section { Marker = startBeat, SectionType = sectionType, Comment = comment };
        }

        int structureStartBeat = reader.ReadInt32();
        uint structureEndBeat = reader.ReadUInt32();
        long afterEndBeatOffset = reader.BaseStream.Position;

        if (!TryReadMusicTrackTail(reader, afterEndBeatOffset, modernTiming: true, out float videoStartTime, out string audioPath) &&
            !TryReadMusicTrackTail(reader, afterEndBeatOffset, modernTiming: false, out videoStartTime, out audioPath))
        {
            throw new InvalidDataException("Could not read legacy MusicTrack timing/audio path.");
        }

        return new MusicTrack
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
                        Path = audioPath,
                        Structure = new Structure
                        {
                            StartBeat = structureStartBeat,
                            EndBeat = checked((int)structureEndBeat),
                            VideoStartTime = videoStartTime,
                            Markers = markers,
                            Signatures = signatures,
                            Sections = sections
                        }
                    }
                }
            ]
        };
    }

    private static ClipTape DeserializeClipTape(Stream stream)
    {
        using BigEndianBinaryReader reader = new(stream);

        uint version = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // Serialized size / tape version
        uint tapeTypeId = reader.ReadUInt32();
        uint tapeTypeSize = reader.ReadUInt32();

        if (version != 1 || tapeTypeId != ClipTapeTypeId)
            throw new InvalidDataException("The stream is not a legacy UbiArt clip tape.");

        int clipCount = reader.ReadInt32();
        List<Clip> clips = new(clipCount);

        for (int i = 0; i < clipCount; i++)
            clips.Add(ReadClip(reader, tapeTypeSize));

        return new ClipTape { Clips = [.. clips] };
    }

    private static Clip ReadClip(BigEndianBinaryReader reader, uint tapeTypeSize)
    {
        uint clipTypeId = reader.ReadUInt32();
        int serializedClipSize = reader.ReadInt32();
        uint id = reader.ReadUInt32();
        uint trackId = reader.ReadUInt32();
        int isActive = reader.ReadInt32();
        int startTime = reader.ReadInt32();
        int duration = reader.ReadInt32();

        Clip clip = clipTypeId switch
        {
            MotionClipTypeId => ReadMotionClip(reader),
            PictogramClipTypeId => ReadPictogramClip(reader),
            GoldEffectClipTypeId => new GoldEffectClip { EffectType = reader.ReadInt32() },
            KaraokeClipTypeId => ReadKaraokeClip(reader),
            SoundSetClipTypeId => ReadSoundSetClip(reader),
            HideUserInterfaceClipTypeId => ReadHideUserInterfaceClip(reader, tapeTypeSize),
            VibrationClipTypeId => ReadVibrationClip(),
            TapeReferenceClipTypeId => ReadTapeReferenceClip(reader),
            _ => ReadUnknownClip(reader, clipTypeId, serializedClipSize)
        };

        clip.Id = id;
        clip.TrackId = trackId;
        clip.IsActive = isActive;
        clip.StartTime = startTime;
        clip.Duration = duration;
        return clip;
    }

    private static VibrationClip ReadVibrationClip() => new()
    {
        VibrationFilePath = DefaultLegacyVibrationPath,
        PlayerId = -1,
        Modulation = 0.5f
    };

    private static TapeReferenceClip ReadTapeReferenceClip(BigEndianBinaryReader reader)
    {
        TapeReferenceClip clip = new()
        {
            Path = ReadPath(reader),
            Loop = reader.ReadInt32()
        };

        SkipBytes(reader, 8); // Legacy tape-reference padding.
        return clip;
    }

    private static UnknownClip ReadUnknownClip(BigEndianBinaryReader reader, uint clipTypeId, int serializedClipSize)
    {
        // Most legacy zero-payload clips store size as the bytes after the type id,
        // which leaves size/clip-header bytes before clip-specific data.
        int payloadSize = Math.Max(0, serializedClipSize - 24);
        byte[] payload = reader.ReadBytes(payloadSize);
        if (payload.Length != payloadSize)
            throw new EndOfStreamException();

        return new UnknownClip
        {
            TypeId = clipTypeId,
            SerializedSize = serializedClipSize,
            Payload = payload
        };
    }

    private static MotionClip ReadMotionClip(BigEndianBinaryReader reader)
    {
        MotionClip clip = new()
        {
            ClassifierPath = ReadPath(reader)
        };

        _ = reader.ReadInt32(); // Unknown0
        clip.GoldMove = reader.ReadInt32();
        clip.CoachId = reader.ReadInt32();
        clip.MoveType = reader.ReadInt32();
        clip.Color = ReadAbgrAsRgba(reader);
        SkipBytes(reader, 64); // LegacyMotionPlatformSpecifics
        return clip;
    }

    private static PictogramClip ReadPictogramClip(BigEndianBinaryReader reader)
    {
        PictogramClip clip = new()
        {
            PictoPath = ReadPath(reader)
        };

        _ = reader.ReadInt32(); // Unknown0
        uint coachCount = reader.ReadUInt32();
        clip.CoachCount = unchecked((int)coachCount);
        return clip;
    }

    private static KaraokeClip ReadKaraokeClip(BigEndianBinaryReader reader)
    {
        return new KaraokeClip
        {
            Pitch = reader.ReadSingle(),
            Lyrics = ReadString(reader),
            IsEndOfLine = reader.ReadInt32(),
            ContentType = reader.ReadInt32(),
            StartTimeTolerance = reader.ReadInt32(),
            EndTimeTolerance = reader.ReadInt32(),
            SemitoneTolerance = reader.ReadSingle()
        };
    }

    private static SoundSetClip ReadSoundSetClip(BigEndianBinaryReader reader)
    {
        return new SoundSetClip
        {
            SoundSetPath = ReadPath(reader),
            SoundChannel = reader.ReadInt32(),
            StopsOnEnd = reader.ReadInt32(),
            AccountedForDuration = reader.ReadInt32(),
            StartOffset = reader.ReadInt32()
        };
    }

    private static HideUserInterfaceClip ReadHideUserInterfaceClip(BigEndianBinaryReader reader, uint tapeTypeSize)
    {
        SkipBytes(reader, tapeTypeSize == 0x8C ? 4 : 8);

        HideUserInterfaceClip clip = new()
        {
            EventType = reader.ReadInt32()
        };

        _ = reader.ReadInt32(); // Padding
        return clip;
    }

    private static uint ReadResourceHeader(BigEndianBinaryReader reader)
    {
        uint version = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // Serialized size
        uint baseTypeId = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // Base type size

        if (version != 1 || baseTypeId != ResourceBaseTypeId)
            throw new InvalidDataException("The stream is not a legacy UbiArt resource.");

        SkipBytes(reader, 28);
        _ = reader.ReadUInt32(); // Component count
        uint componentTypeId = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // Component size
        return componentTypeId;
    }

    private static void SkipSongDescTags(BigEndianBinaryReader reader)
    {
        for (int i = 0; i < 14; i++)
            _ = reader.ReadUInt32();
    }

    private static int[] ReadInt32Array(BigEndianBinaryReader reader, int count)
    {
        if (count < 0)
            throw new InvalidDataException("Negative array count in legacy binary data.");

        int[] values = new int[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = reader.ReadInt32();

        return values;
    }

    private static bool TryReadMusicTrackTail(BigEndianBinaryReader reader, long afterEndBeatOffset, bool modernTiming, out float videoStartTime, out string audioPath)
    {
        videoStartTime = 0;
        audioPath = string.Empty;

        try
        {
            reader.BaseStream.Position = afterEndBeatOffset;
            if (modernTiming)
                SkipBytes(reader, 10);

            videoStartTime = reader.ReadSingle();
            SkipBytes(reader, modernTiming ? 20 : 4);

            audioPath = ReadPath(reader);
            if (!IsPlausibleAudioPath(audioPath))
                return false;

            SkipBytes(reader, 8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlausibleAudioPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wem", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadPath(BigEndianBinaryReader reader)
    {
        string fileName = ReadString(reader);
        string folder = ReadString(reader);
        _ = reader.ReadUInt32(); // Resource id

        folder = folder.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(folder))
            return fileName;

        return $"{folder.TrimEnd('/')}/{fileName}";
    }

    private static float[] ReadAbgrAsRgba(BigEndianBinaryReader reader)
    {
        float alpha = reader.ReadSingle();
        float blue = reader.ReadSingle();
        float green = reader.ReadSingle();
        float red = reader.ReadSingle();

        return [alpha, red, green, blue];
    }

    private static string ReadString(BigEndianBinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 1_048_576)
            throw new InvalidDataException($"Invalid legacy UbiArt string length {length}.");

        if (length == 0)
            return string.Empty;

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();

        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static void SkipBytes(BinaryReader reader, int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException();
    }
}
