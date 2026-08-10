using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization;

using System.Reflection;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal sealed class UncookedGameplayContentBuilder(UbiArtEngineVersion version)
{
    private const long PictoTrackId = 1272115770L;
    private const long GoldEffectTrackId = 628418524L;

    private static byte[] ToBytes(string content) => Encoding.UTF8.GetBytes(content);

    public object GenerateSongDesc(IntermediateSongPackage package)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string mapRoot = GetMapRoot(mapNameLower);
        List<object> phoneImages = [new { KEY = "cover", VAL = $"{mapRoot}/menuart/textures/{mapNameLower}_cover_phone.png" }];
        phoneImages.AddRange(Enumerable.Range(1, package.Metadata.CoachCount)
            .Select(index => (object)new { KEY = $"coach{index}", VAL = $"{mapRoot}/menuart/textures/{mapNameLower}_coach_{index}_phone.png" }));

        List<object> defaultColors =
        [
            new { KEY = "lyrics", VAL = ToUbiArtColor(package.Metadata.LyricsColor) },
            new { KEY = "theme", VAL = "0xffffffff" }
        ];
        AddSongColor(defaultColors, package, "songcolor_1a", "songColor_1A");
        AddSongColor(defaultColors, package, "songcolor_1b", "songColor_1B");
        AddSongColor(defaultColors, package, "songcolor_2a", "songColor_2A");
        AddSongColor(defaultColors, package, "songcolor_2b", "songColor_2B");

        object songInfo = new
        {
            MapName = mapName,
            JDVersion = (int)version,
            package.Metadata.OriginalJDVersion,
            RelatedAlbums = Array.Empty<object>(),
            package.Metadata.Artist,
            package.Metadata.Title,
            package.Metadata.Credits,
            PhoneImages = phoneImages,
            NumCoach = package.Metadata.CoachCount,
            MainCoach = -1,
            package.Metadata.Difficulty,
            package.Metadata.SweatDifficulty,
            Tags = package.Metadata.Tags.Select(tag => new { VAL = tag }).ToArray(),
            package.Metadata.Status,
            package.Metadata.MojoValue,
            package.Metadata.CountInProgression,
            GameModes = new[]
            {
                new
                {
                    NAME = "GameModeDesc",
                    GameModeDesc = new
                    {
                        mode = new LuaExpression("GameMode.Classic"),
                        flags = new LuaExpression("GameModeFlags.None"),
                        status = new LuaExpression("GameModeStatus.Available")
                    }
                }
            },
            DefaultColors = defaultColors,
            AudioPreviewFadeTime = 0.5,
            AudioPreviews = new object[]
            {
                new { NAME = "AudioPreview", AudioPreview = new { name = "coverflow", startbeat = package.TimelineStructure.PreviewEntryBeat } },
                new { NAME = "AudioPreview", AudioPreview = new { name = "prelobby", startbeat = package.TimelineStructure.PreviewLoopStartBeat, endbeat = package.TimelineStructure.PreviewLoopEndBeat } }
            }
        };

        object document = new
        {
            NAME = "Actor_Template",
            Actor_Template = new
            {
                TAGS = new[] { new { VAL = "songdescmain" } },
                COMPONENTS = new[] { new { NAME = "JD_SongDescTemplate", JD_SongDescTemplate = songInfo } }
            }
        };
        return LuaDocumentWriter.Write(document, includes: ["EngineData/Helpers/SongDatabase.ilu"]);
    }

    public object GenerateMusicTrack(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        object document = new
        {
            NAME = "Actor_Template",
            Actor_Template = new
            {
                COMPONENTS = new[]
                {
                    new
                    {
                        NAME = "MusicTrackComponent_Template",
                        MusicTrackComponent_Template = new
                        {
                            trackData = new
                            {
                                MusicTrackData = new
                                {
                                    path = $"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}.wav",
                                    structure = new LuaExpression("structure"),
                                    volume = 0
                                }
                            }
                        }
                    }
                }
            }
        };
        return Lua(document, includes: [$"{GetMapRoot(mapNameLower)}/audio/{mapNameLower}.trk"]);
    }

    public object GenerateDanceTape(IntermediateSongPackage package)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<object> allClips = [];
        List<object> tracks = [];
        HashSet<long> moveTrackIds = [];

        // Add MotionClips for each coach
        foreach (MoveTimeline timeline in package.CoachTimelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (!package.HandCoachMoves.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    continue;

                allClips.Add(new
                {
                    NAME = "MotionClip",
                    MotionClip = new
                    {
                        clip.Id,
                        timeline.TrackId,
                        IsActive = 1,
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"{GetMapRoot(mapNameLower)}/timeline/moves/{clip.MoveId}.msm",
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        timeline.CoachId,
                        MoveType = 0,
                        Color = ParseColorToHex(move.Color)
                    }
                });
            }

            // Add track for this coach
            if (moveTrackIds.Add(timeline.TrackId))
            {
                tracks.Add(new
                {
                    NAME = "MoveTrack",
                    MoveTrack = new
                    {
                        Id = timeline.TrackId,
                        Name = $"Coach{timeline.CoachId}"
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

                allClips.Add(new
                {
                    NAME = "MotionClip",
                    MotionClip = new
                    {
                        clip.Id,
                        timeline.TrackId,
                        IsActive = 1,
                        clip.StartTime,
                        move.Duration,
                        ClassifierPath = $"{GetMapRoot(mapNameLower)}/timeline/moves/{clip.MoveId}.gesture",
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        timeline.CoachId,
                        MoveType = 1,
                        Color = ParseColorToHex(move.Color)
                    }
                });
            }

            if (moveTrackIds.Add(timeline.TrackId))
            {
                tracks.Add(new
                {
                    NAME = "MoveTrack",
                    MoveTrack = new
                    {
                        Id = timeline.TrackId,
                        Name = $"Coach{timeline.CoachId}"
                    }
                });
            }
        }

        // Add PictogramClips
        foreach (PictogramClip pictoClip in package.Pictograms.Clips)
        {
            allClips.Add(new
            {
                NAME = "PictogramClip",
                PictogramClip = new
                {
                    pictoClip.Id,
                    TrackId = PictoTrackId,
                    pictoClip.StartTime,
                    pictoClip.Duration,
                    PictoPath = $"{GetMapRoot(mapNameLower)}/timeline/pictos/{pictoClip.PictogramId}{(version == UbiArtEngineVersion.JD2014 ? ".tga" : ".png")}",
                }
            });
        }

        // Add GoldEffectClips
        foreach (GoldEffectClip goldClip in package.GoldEffects.Clips)
        {
            allClips.Add(new
            {
                NAME = "GoldEffectClip",
                GoldEffectClip = new
                {
                    goldClip.Id,
                    TrackId = GoldEffectTrackId,
                    goldClip.StartTime,
                    goldClip.Duration,
                    goldClip.EffectType
                }
            });
        }

        // Add PictoTrack
        tracks.Add(new
        {
            NAME = "PictoTrack",
            PictoTrack = new
            {
                Id = PictoTrackId,
                Name = "Pictos"
            }
        });

        // Add GoldEffectTrack
        tracks.Add(new
        {
            NAME = "GoldEffectTrack",
            GoldEffectTrack = new
            {
                Id = GoldEffectTrackId,
                Name = "GoldMoveEffects"
            }
        });

        var danceTape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = allClips.OrderBy(c => GetStartTime(c)).ToArray(),
                Tracks = tracks.ToArray(),
                TapeClock = 0,
                package.Metadata.MapName
            }
        };

        return Lua(danceTape);
    }

    public object GenerateKaraokeTape(IntermediateSongPackage package)
    {
        var clips = package.Lyrics.Clips.Select(c => new
        {
            NAME = "KaraokeClip",
            KaraokeClip = new
            {
                c.Id,
                TrackId = 0,
                IsActive = 1,
                c.StartTime,
                c.Duration,
                c.Lyrics,
                Pitch = c.Pitch > 0 ? c.Pitch : 8.175798,
                IsEndOfLine = c.IsEndOfLine ? 1 : 0,
                ContentType = c.ContentType > 0 ? c.ContentType : 2
            }
        }).OrderBy(c => c.KaraokeClip.StartTime).ToArray();

        var karaokeTape = new
        {
            NAME = "Tape",
            Tape = new
            {
                Clips = clips,
                TapeClock = 0,
                package.Metadata.MapName
            }
        };

        return Lua(karaokeTape);
    }

    private string GetMapRoot(string mapNameLower) => version switch
    {
        UbiArtEngineVersion.JD2014 => $"world/jd5/{mapNameLower}",
        UbiArtEngineVersion.JD2015 => $"world/jd2015/{mapNameLower}",
        _ => $"world/maps/{mapNameLower}"
    };

    private static string ParseColorToHex(string hexColor)
    {
        return ToUbiArtColor(hexColor);
    }

    private static void AddSongColor(List<object> colors, IntermediateSongPackage package, string metadataKey, string ubiArtKey)
    {
        if (package.Metadata.AdditionalMetadata.TryGetValue(metadataKey, out string? color) && !string.IsNullOrWhiteSpace(color))
            colors.Add(new { KEY = ubiArtKey, VAL = ToUbiArtColor(color) });
    }

    private static byte[] Lua(object value, string assignment = "params", IEnumerable<string>? includes = null) =>
        ToBytes(LuaDocumentWriter.Write(value, assignment, includes));

    private static string ToUbiArtColor(string color)
    {
        string hex = color.Trim().TrimStart('#');
        return hex.Length switch
        {
            8 => $"0x{hex[6..8]}{hex[..6]}",
            6 => $"0xFF{hex}",
            _ => "0xFFFF8080"
        };
    }

    private static int GetStartTime(object clip)
    {
        // Use reflection to get StartTime from the anonymous type
        Type type = clip.GetType();
        PropertyInfo? nameProperty = type.GetProperty("NAME");
        if (nameProperty?.GetValue(clip) is string name)
        {
            PropertyInfo? innerProperty = type.GetProperty(name);
            if (innerProperty?.GetValue(clip) is object inner)
            {
                PropertyInfo? startTimeProperty = inner.GetType().GetProperty("StartTime");
                if (startTimeProperty?.GetValue(inner) is int startTime)
                    return startTime;
            }
        }

        return 0;
    }
}
