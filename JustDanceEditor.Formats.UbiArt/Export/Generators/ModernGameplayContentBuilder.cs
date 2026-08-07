using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal sealed class ModernGameplayContentBuilder(UbiArtEngineVersion EngineVersion)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);

    public object GenerateSongDesc(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        var songDesc = new
        {
            __class = "Actor_Template",
            WIP = 0,
            LOWUPDATE = 0,
            UPDATE_LAYER = 0,
            PROCEDURAL = 0,
            STARTPAUSED = 0,
            FORCEISENVIRONMENT = 0,
            COMPONENTS = new object[]
            {
                new
                {
                    __class = "JD_SongDescTemplate",
                    package.Metadata.MapName,
                    JDVersion = (int)EngineVersion,
                    package.Metadata.OriginalJDVersion,
                    package.Metadata.Artist,
                    DancerName = "Unknown Dancer",
                    package.Metadata.Title,
                    Credits = package.Metadata.Credits ?? "",
                    PhoneImages = new
                    {
                        cover = $"world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_cover_phone.jpg",
                        coach1 = $"world/maps/{mapNameLower}/menuart/textures/{mapNameLower}_coach_1_phone.png"
                    },
                    NumCoach = package.Metadata.CoachCount,
                    MainCoach = -1,
                    package.Metadata.Difficulty,
                    package.Metadata.SweatDifficulty,
                    backgroundType = 0,
                    LyricsType = 0,
                    Tags = new[] { "main" },
                    Status = 3,
                    LocaleID = 4294967295,
                    MojoValue = 0,
                    CountInProgression = 1,
                    DefaultColors = new
                    {
                        songcolor_1a = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1a", "#FFFFFFFF")),
                        songcolor_1b = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1b", "#FFFFFFFF")),
                        songcolor_2a = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2a", "#FFFFFFFF")),
                        songcolor_2b = ConvertColorToArray(package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2b", "#FFFFFFFF")),
                        lyrics = ConvertColorToArray(package.Metadata.LyricsColor),
                        theme = new[] { 1, 1, 1, 1 }
                    }
                }
            }
        };
        return ToBytes(JsonSerializer.Serialize(songDesc, JsonOptions));
    }

    public object GenerateMusicTrack(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();

        Signature[] signatures = [.. package.TimelineStructure.Signatures.Select(s => new Signature { Beats = s.Beats, Marker = s.Marker })];
        Section[] sections = [.. package.TimelineStructure.Sections.Select(sec => new Section { Marker = (float)sec.StartBeat, SectionType = (int)sec.SectionType, Comment = sec.Comment ?? string.Empty })];

        Structure structure = new()
        {
            StartBeat = package.TimelineStructure.StartBeat,
            EndBeat = package.TimelineStructure.EndBeat,
            VideoStartTime = (float)package.TimelineStructure.VideoStartOffset,
            PreviewEntry = package.TimelineStructure.PreviewEntryBeat,
            PreviewLoopStart = package.TimelineStructure.PreviewLoopStartBeat,
            PreviewLoopEnd = package.TimelineStructure.PreviewLoopEndBeat,
            PreviewDuration = package.TimelineStructure.PreviewDuration,
            Markers = [.. package.TimelineStructure.Markers],
            Signatures = signatures,
            Sections = sections
        };

        MusicTrack musicTrack = new()
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
                        Structure = structure,
                        Path = $"world/maps/{mapNameLower}/audio/{mapNameLower}.wav",
                        Url = $"jmcs://jd-contents/{mapName}/{mapName}.ogg"
                    }
                }
            ]
        };

        return ToBytes(JsonSerializer.Serialize(musicTrack, JsonOptions));
    }

    public object GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> clips = [];

        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;
                clips.Add(new
                {
                    __class = "MotionClip",
                    clip.Id,
                    timeline.TrackId,
                    IsActive = 1,
                    clip.StartTime,
                    move.Duration,
                    ClassifierPath = $"world/maps/{mapNameLower}/timeline/moves/{clip.MoveId}.msm",
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    timeline.CoachId,
                    MoveType = 0,
                    Color = ParseColorToRgba(move.Color),
                    MotionPlatformSpecifics = new
                    {
                        X360 = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 },
                        ORBIS = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = -0.2, HighThreshold = 0.6 },
                        DURANGO = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 }
                    }
                });
            }
        }

        foreach (MoveTimeline timeline in package.FullBodyCoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.FullBodyCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                clips.Add(new
                {
                    __class = "MotionClip",
                    clip.Id,
                    timeline.TrackId,
                    IsActive = 1,
                    clip.StartTime,
                    move.Duration,
                    ClassifierPath = $"world/maps/{mapNameLower}/timeline/moves/{clip.MoveId}.gesture",
                    GoldMove = clip.IsGoldMove ? 1 : 0,
                    timeline.CoachId,
                    MoveType = 1,
                    Color = ParseColorToRgba(move.Color),
                    MotionPlatformSpecifics = new
                    {
                        X360 = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 },
                        ORBIS = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = -0.2, HighThreshold = 0.6 },
                        DURANGO = new { __class = "MotionPlatformSpecific", ScoreScale = 1, ScoreSmoothing = 0, LowThreshold = 0.2, HighThreshold = 1.0 }
                    }
                });
            }
        }

        foreach (PictogramClip pictoClip in package.Pictograms.Clips)
        {
            clips.Add(new
            {
                __class = "PictogramClip",
                pictoClip.Id,
                TrackId = 1272115770L,
                IsActive = 1,
                pictoClip.StartTime,
                pictoClip.Duration,
                PictoPath = $"world/maps/{mapNameLower}/timeline/pictos/{pictoClip.PictogramId}.png",
                CoachCount = 4294967295u
            });
        }

        foreach (GoldEffectClip goldClip in package.GoldEffects.Clips)
        {
            clips.Add(new
            {
                __class = "GoldEffectClip",
                goldClip.Id,
                TrackId = 628418524L,
                IsActive = 1,
                goldClip.StartTime,
                goldClip.Duration,
                goldClip.EffectType
            });
        }

        var dtape = new
        {
            __class = "Tape",
            Clips = clips.OrderBy(c => ((dynamic)c).StartTime).ToList(),
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            MapName = mapNameLower,
            SoundwichEvent = ""
        };

        return ToBytes(JsonSerializer.Serialize(dtape, JsonOptions));
    }

    public object GenerateKaraokeTape(IntermediateSongPackage package)
    {
        var clips = package.Lyrics.Clips.Select(lyric => new
        {
            __class = "KaraokeClip",
            lyric.Id,
            TrackId = 0,
            IsActive = 1,
            lyric.StartTime,
            lyric.Duration,
            Pitch = lyric.Pitch > 0 ? lyric.Pitch : 8.175798,
            lyric.Lyrics,
            IsEndOfLine = lyric.IsEndOfLine ? 1 : 0,
            ContentType = lyric.ContentType > 0 ? lyric.ContentType : 2,
            StartTimeTolerance = lyric.Tolerances?.StartTimeTolerance ?? 4,
            EndTimeTolerance = lyric.Tolerances?.EndTimeTolerance ?? 4,
            SemitoneTolerance = lyric.Tolerances?.SemitoneTolerance ?? 5
        }).OrderBy(c => c.StartTime).ToList();

        var ktape = new
        {
            __class = "Tape",
            Clips = clips,
            TapeClock = 0,
            TapeBarCount = 1,
            FreeResourcesAfterPlay = 0,
            package.Metadata.MapName,
            SoundwichEvent = ""
        };

        return ToBytes(JsonSerializer.Serialize(ktape, JsonOptions));
    }

    private static float[] ConvertColorToArray(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return [1.0f, 1.0f, 1.0f, 1.0f];
        try
        {
            string hex = hexColor.TrimStart('#');
            return [
                int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber) / 255.0f,
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber) / 255.0f
            ];
        }
        catch
        {
            return [1.0f, 1.0f, 1.0f, 1.0f];
        }
    }

    private static double[] ParseColorToRgba(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor.Length < 7)
            return [1.0, 0.5, 0.5, 0.5];
        string hex = hexColor.TrimStart('#');
        return [
            1.0,
            Convert.ToInt32(hex[..2], 16) / 255.0,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0
        ];
    }

}

