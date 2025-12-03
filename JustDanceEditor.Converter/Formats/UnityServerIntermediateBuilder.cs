using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Converter.Intermediate;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.JDI.Manifests;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Tapes;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using IntermediateKaraokeClip = JustDanceEditor.Formats.JDI.Timelines.KaraokeClip;

namespace JustDanceEditor.Converter.Formats;

internal static class UnityServerIntermediateBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IntermediateSongPackage FromServerExport(string mapRoot)
    {
        if (string.IsNullOrWhiteSpace(mapRoot))
            throw new ArgumentException("Map root cannot be empty.", nameof(mapRoot));

        string songInfoPath = Path.Combine(mapRoot, "SongInfo.json");
        if (!File.Exists(songInfoPath))
            throw new FileNotFoundException($"Unity server export missing SongInfo.json at '{songInfoPath}'.");

        UnitySongInfo songInfo = LoadSongInfo(songInfoPath);
        UnityMapPackageData mapData = LoadMapPackageData(mapRoot);
        TimelineMath timelineMath = new(mapData.Structure);

        (
            List<CoachTimelineDocument> handTimelines,
            List<CoachTimelineDocument> fullBodyTimelines,
            Dictionary<string, CoachMoveDefinition> handMoves,
            Dictionary<string, CoachMoveDefinition> fullBodyMoves) =
            BuildCoachTimelines(mapData.MotionClips);

        IntermediateSongPackage package = new()
        {
            Manifest = new IntermediatePackageManifest(),
            Metadata = BuildMetadata(songInfo, mapData.Structure, songInfoPath, mapData.MapPackagePath),
            AssetCatalog = BuildAssetCatalog(mapRoot, mapData.MapPackagePath),
            TimelineStructure = BuildTimelineStructure(mapData.Structure, timelineMath),
            Lyrics = BuildLyricsDocument(mapData.KaraokeClips),
            Pictograms = BuildPictogramDocument(mapData.PictogramClips),
            Events = BuildEventDocument(mapData),
            CoachTimelines = handTimelines,
            FullBodyCoachTimelines = fullBodyTimelines,
            HandCoachMoves = handMoves,
            FullBodyCoachMoves = fullBodyMoves
        };

        package.Metadata.Validate();
        return package;
    }

    private static UnitySongInfo LoadSongInfo(string songInfoPath)
    {
        string json = File.ReadAllText(songInfoPath);
        UnitySongInfo? info = JsonSerializer.Deserialize<UnitySongInfo>(json, JsonOptions);
        if (info == null)
            throw new InvalidDataException($"Failed to deserialize SongInfo.json located at '{songInfoPath}'.");
        return info;
    }

    private static UnityMapPackageData LoadMapPackageData(string mapRoot)
    {
        string mapPackagePath = LocateMapPackageBundle(mapRoot);
        AssetsManager manager = new();

        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(mapPackagePath, true);
            AssetsFileInstance assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            AssetsFile assets = assetsFile.file;
            assets.GenerateQuickLookup();

            AssetFileInfo[] monoInfos = [.. assets.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
            if (monoInfos.Length < 2)
                throw new InvalidDataException("MapPackage bundle does not contain the required MonoBehaviours.");

            AssetTypeValueField? musicTrack = null;
            AssetTypeValueField? mapBehaviour = null;

            foreach (AssetFileInfo info in monoInfos)
            {
                AssetTypeValueField field = manager.GetBaseField(assetsFile, info);
                if (string.IsNullOrWhiteSpace(field["m_Name"].AsString) && musicTrack == null)
                {
                    musicTrack = field;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(field["m_Name"].AsString))
                {
                    mapBehaviour ??= field;
                }

                if (musicTrack != null && mapBehaviour != null)
                    break;
            }

            if (musicTrack == null || mapBehaviour == null)
                throw new InvalidDataException("Could not locate MusicTrack and MapBehaviour MonoBehaviours inside the MapPackage.");

            Structure structure = ParseStructure(musicTrack);

            List<UnityKaraokeClip> karaoke = ParseKaraokeClips(mapBehaviour);
            List<UnityMotionClip> motions = ParseMotionClips(mapBehaviour);
            List<UnityPictoClip> pictos = ParsePictoClips(mapBehaviour);
            List<UnityGoldEffectClip> goldEffects = ParseGoldEffects(mapBehaviour);
            List<UnityHideHudClip> hideHud = ParseHideHudClips(mapBehaviour);

            return new UnityMapPackageData(mapPackagePath, structure, karaoke, motions, pictos, goldEffects, hideHud);
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static Structure ParseStructure(AssetTypeValueField musicTrackBase)
    {
        AssetTypeValueField structureField = musicTrackBase["m_structure"]["MusicTrackStructure"];

        static int ReadInt(AssetTypeValueField parent, string name) => parent[name].IsDummy ? 0 : parent[name].AsInt;
        static double ReadDouble(AssetTypeValueField parent, string name) => parent[name].IsDummy ? 0 : parent[name].AsDouble;
        static bool ReadBool(AssetTypeValueField parent, string name) => !parent[name].IsDummy && parent[name].AsBool;

        Structure structure = new()
        {
            startBeat = ReadInt(structureField, "startBeat"),
            endBeat = ReadInt(structureField, "endBeat"),
            videoStartTime = (float)ReadDouble(structureField, "videoStartTime"),
            previewEntry = (int)Math.Round(ReadDouble(structureField, "previewEntry")),
            previewLoopStart = (int)Math.Round(ReadDouble(structureField, "previewLoopStart")),
            previewLoopEnd = (int)Math.Round(ReadDouble(structureField, "previewLoopEnd")),
            fadeStartBeat = ReadInt(structureField, "fadeStartBeat"),
            fadeEndBeat = ReadInt(structureField, "fadeEndBeat"),
            useFadeStartBeat = ReadBool(structureField, "useFadeStartBeat"),
            useFadeEndBeat = ReadBool(structureField, "useFadeEndBeat"),
            fadeInType = (float)ReadDouble(structureField, "fadeInType"),
            fadeOutType = ReadInt(structureField, "fadeOutType")
        };

        structure.markers = ReadArray(structureField["markers"]["Array"], field => (int)field["VAL"].AsLong);
        structure.signatures = ReadArray(structureField["signatures"]["Array"], field => new Signature
        {
            beats = field["MusicSignature"]["beats"].AsInt,
            marker = (float)field["MusicSignature"]["marker"].AsDouble
        });

        structure.sections = ReadArray(structureField["sections"]["Array"], field => new Section
        {
            sectionType = field["MusicSection"]["sectionType"].AsInt,
            marker = (float)field["MusicSection"]["marker"].AsDouble,
            comment = field["MusicSection"]["comment"].AsString
        });

        return structure;
    }

    private static List<UnityKaraokeClip> ParseKaraokeClips(AssetTypeValueField mapBehaviour)
    {
        AssetTypeValueField array = mapBehaviour["KaraokeData"]["Clips"]["Array"];
        List<UnityKaraokeClip> clips = new(array?.Children.Count ?? 0);

        foreach (AssetTypeValueField entry in Enumerate(array))
        {
            AssetTypeValueField clipField = entry["KaraokeClip"];
            clips.Add(new UnityKaraokeClip(
                clipField["StartTime"].AsInt,
                clipField["Duration"].AsInt,
                clipField["Lyrics"].AsString,
                clipField["Pitch"].AsFloat,
                clipField["IsEndOfLine"].AsUInt > 0,
                clipField["ContentType"].AsInt,
                clipField["Id"].AsLong,
                clipField["TrackId"].AsLong,
                clipField["IsActive"].AsUInt > 0,
                clipField["SemitoneTolerance"].IsDummy ? 0 : clipField["SemitoneTolerance"].AsInt,
                clipField["StartTimeTolerance"].IsDummy ? 0 : clipField["StartTimeTolerance"].AsInt,
                clipField["EndTimeTolerance"].IsDummy ? 0 : clipField["EndTimeTolerance"].AsInt
            ));
        }

        return clips;
    }

    private static List<UnityMotionClip> ParseMotionClips(AssetTypeValueField mapBehaviour)
    {
        AssetTypeValueField array = mapBehaviour["DanceData"]["MotionClips"]["Array"];
        List<UnityMotionClip> clips = new(array?.Children.Count ?? 0);

        foreach (AssetTypeValueField entry in Enumerate(array))
        {
            string color = entry["Color"].IsDummy ? string.Empty : entry["Color"].AsString;
            clips.Add(new UnityMotionClip(
                entry["StartTime"].AsInt,
                entry["Duration"].AsInt,
                entry["MoveName"].AsString,
                entry["CoachId"].AsInt,
                entry["GoldMove"].AsUInt > 0,
                entry["MoveType"].AsInt,
                entry["Id"].AsLong,
                entry["TrackId"].AsLong,
                entry["IsActive"].AsUInt > 0,
                color
            ));
        }

        return clips;
    }

    private static List<UnityPictoClip> ParsePictoClips(AssetTypeValueField mapBehaviour)
    {
        AssetTypeValueField array = mapBehaviour["DanceData"]["PictoClips"]["Array"];
        List<UnityPictoClip> clips = new(array?.Children.Count ?? 0);

        foreach (AssetTypeValueField entry in Enumerate(array))
        {
            clips.Add(new UnityPictoClip(
                entry["StartTime"].AsInt,
                entry["Duration"].AsInt,
                entry["PictoPath"].AsString,
                (int)entry["CoachCount"].AsUInt,
                entry["Id"].AsLong,
                entry["TrackId"].AsLong,
                entry["IsActive"].AsUInt > 0
            ));
        }

        return clips;
    }

    private static List<UnityGoldEffectClip> ParseGoldEffects(AssetTypeValueField mapBehaviour)
    {
        AssetTypeValueField array = mapBehaviour["DanceData"]["GoldEffectClips"]["Array"];
        List<UnityGoldEffectClip> clips = new(array?.Children.Count ?? 0);

        foreach (AssetTypeValueField entry in Enumerate(array))
        {
            clips.Add(new UnityGoldEffectClip(
                entry["StartTime"].AsInt,
                entry["Duration"].AsInt,
                entry["GoldEffectType"].AsInt,
                entry["Id"].AsLong,
                entry["TrackId"].AsLong,
                entry["IsActive"].AsUInt > 0
            ));
        }

        return clips;
    }

    private static List<UnityHideHudClip> ParseHideHudClips(AssetTypeValueField mapBehaviour)
    {
        AssetTypeValueField array = mapBehaviour["DanceData"]["HideHudClips"]["Array"];
        List<UnityHideHudClip> clips = new(array?.Children.Count ?? 0);

        foreach (AssetTypeValueField entry in Enumerate(array))
        {
            clips.Add(new UnityHideHudClip(
                entry["StartTime"].AsInt,
                entry["Duration"].AsInt,
                entry["Id"].IsDummy ? 0 : entry["Id"].AsLong,
                entry["TrackId"].IsDummy ? 0 : entry["TrackId"].AsLong,
                entry["IsActive"].AsUInt > 0,
                entry["EventType"].IsDummy ? 0 : entry["EventType"].AsInt,
                entry["CustomParam"].IsDummy ? string.Empty : entry["CustomParam"].AsString
            ));
        }

        return clips;
    }

    private static IntermediateMetadata BuildMetadata(UnitySongInfo info, Structure structure, string songInfoPath, string mapPackagePath)
    {
        double mapLengthSeconds = CalculateMapLengthSeconds(structure);

        IntermediateMetadata metadata = new()
        {
            SongId = info.SongId == Guid.Empty ? Guid.NewGuid() : info.SongId,
            MapName = info.MapName ?? string.Empty,
            ParentMapName = string.IsNullOrWhiteSpace(info.ParentMapName) ? info.MapName ?? string.Empty : info.ParentMapName,
            Title = info.Title ?? string.Empty,
            Artist = info.Artist ?? string.Empty,
            Credits = info.Credits ?? string.Empty,
            LyricsColor = string.IsNullOrWhiteSpace(info.LyricsColor) ? "#FFFFFFFF" : info.LyricsColor,
            MapLengthSeconds = mapLengthSeconds > 0 ? mapLengthSeconds : info.MapLength,
            EngineVersion = info.OriginalJdVersion,
            OriginalJdVersion = info.OriginalJdVersion,
            CoachCount = info.CoachCount,
            Difficulty = info.Difficulty,
            SweatDifficulty = info.SweatDifficulty,
            Tags = info.Tags?.ToList() ?? [],
            Status = 1f,
            HasSongTitleInCover = info.HasSongTitleInCover,
            MojoValue = 0,
            CountInProgression = 0
        };

        if (info.CoachNames is { Length: > 0 })
            metadata.CoachNames = info.CoachNames;

        metadata.AdditionalMetadata["unity.tagIds"] = string.Join(',', info.TagIds ?? Array.Empty<string>());
        metadata.AdditionalMetadata["unity.coachNamesLocIds"] = string.Join(',', info.CoachNamesLocIds ?? Array.Empty<string>());
        metadata.AdditionalMetadata["unity.danceVersionLocId"] = info.DanceVersionLocId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        metadata.AdditionalMetadata["unity.doubleScoringType"] = info.DoubleScoringType ?? string.Empty;

        return metadata;
    }

    private static IntermediateAssetCatalog BuildAssetCatalog(string mapRoot, string mapPackagePath)
    {
        IntermediateAssetCatalog catalog = new();

        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "Audio_opus"), "*.opus", "audio/master");
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "AudioPreview_opus"), "*.opus", "audio/preview", required: false);
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "video"), "*.webm", "video/background", required: false, allowFallback: true);
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "videoPreview"), "*.webm", "video/preview", required: false, allowFallback: true);
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "Cover"), "*.bundle", "image/cover");
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "songTitleLogo"), "*.bundle", "image/songTitleLogo", required: false);
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "CoachesLarge"), "*.bundle", "image/coachLarge");
        AddSingleFileAsset(catalog, Path.Combine(mapRoot, "MapPackage"), "*.bundle", "bundle/mapPackage", allowFallback: true, explicitPath: mapPackagePath);

        IntermediateAsset motionScripts = catalog.Add("motion/msm");
        motionScripts.Required = false;

        IntermediateAsset gestureScripts = catalog.Add("motion/gestures");
        gestureScripts.Required = false;

        return catalog;
    }

    private static TimelineStructureDocument BuildTimelineStructure(Structure structure, TimelineMath timelineMath)
    {
        TimelineStructureDocument document = new()
        {
            TimeBaseMsPerBeat = timelineMath.EstimateMsPerBeat(),
            AudioStartOffset = ComputeSongStartOffset(structure),
            VideoStartOffset = structure.videoStartTime,
            PreviewEntryBeat = structure.previewEntry,
            PreviewLoopStartBeat = structure.previewLoopStart,
            PreviewLoopEndBeat = structure.previewLoopEnd
        };

        if (structure.markers is { Length: > 0 })
        {
            for (int i = 0; i < structure.markers.Length; i++)
            {
                document.Markers.Add(new TimelineMarker
                {
                    BeatIndex = i,
                    TimeMs = (int)Math.Round(structure.markers[i] / 48d)
                });
            }
        }

        document.TempoSegments.Add(new TempoSegment
        {
            StartBeat = structure.startBeat,
            BeatsPerMinute = timelineMath.EstimateBpm()
        });

        if (structure.signatures is { Length: > 0 })
        {
            foreach (Signature signature in structure.signatures)
            {
                document.Signatures.Add(new SignatureSegment
                {
                    StartBeat = signature.marker,
                    Numerator = signature.beats,
                    Denominator = 4
                });
            }
        }

        if (structure.sections is { Length: > 0 })
        {
            foreach (Section section in structure.sections)
            {
                document.Sections.Add(new SectionSegment
                {
                    StartBeat = section.marker,
                    SectionType = section.sectionType.ToString(CultureInfo.InvariantCulture),
                    Comment = section.comment
                });
            }
        }

        if (structure.useFadeStartBeat)
        {
            document.FadeIn = new FadeRegion
            {
                StartBeat = structure.fadeStartBeat,
                Duration = Math.Max(0, structure.fadeEndBeat - structure.fadeStartBeat),
                CurveType = structure.fadeInType == 0 ? "linear" : "custom"
            };
        }

        if (structure.useFadeEndBeat)
        {
            document.FadeOut = new FadeRegion
            {
                StartBeat = structure.fadeEndBeat,
                Duration = Math.Max(0, structure.endBeat - structure.fadeEndBeat),
                CurveType = structure.fadeOutType == 0 ? "linear" : "custom"
            };
        }

        return document;
    }

    private static LyricsTimelineDocument BuildLyricsDocument(IEnumerable<UnityKaraokeClip> clips)
    {
        LyricsTimelineDocument document = new();

        foreach (UnityKaraokeClip clip in clips.OrderBy(c => c.StartTime))
        {
            IntermediateKaraokeClip entry = new()
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                Lyrics = clip.Lyrics,
                Pitch = clip.Pitch,
                IsEndOfLine = clip.IsEndOfLine,
                ContentType = clip.ContentType,
                Tolerances = BuildTolerance(clip)
            };

            document.Clips.Add(entry);
        }

        return document;
    }

    private static KaraokeTolerance? BuildTolerance(UnityKaraokeClip clip)
    {
        if (clip.StartTimeTolerance == 0 && clip.EndTimeTolerance == 0 && clip.SemitoneTolerance == 0)
            return null;

        return new KaraokeTolerance
        {
            StartTimeTolerance = Math.Abs(clip.StartTimeTolerance),
            EndTimeTolerance = Math.Abs(clip.EndTimeTolerance),
            SemitoneTolerance = clip.SemitoneTolerance
        };
    }

    private static PictogramTimelineDocument BuildPictogramDocument(IEnumerable<UnityPictoClip> clips)
    {
        PictogramTimelineDocument document = new();

        foreach (UnityPictoClip clip in clips.OrderBy(c => c.StartTime))
        {
            document.Entries.Add(new PictogramEntry
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                Duration = Math.Max(0, clip.Duration),
                PictogramId = clip.PictoName,
                CoachCount = clip.CoachCount
            });
        }

        return document;
    }

    private static EventTimelineDocument BuildEventDocument(UnityMapPackageData data)
    {
        EventTimelineDocument document = new();

        foreach (UnityGoldEffectClip clip in data.GoldEffectClips.OrderBy(c => c.StartTime))
        {
            int duration = Math.Max(0, clip.Duration);
            GoldEffectClip payload = new()
            {
                EffectType = clip.GoldEffectType,
                Id = clip.Id,
                TrackId = clip.TrackId,
                IsActive = clip.IsActive ? 1 : 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration
            };

            document.Events.Add(new TimelineEvent
            {
                Id = clip.Id,
                Type = nameof(GoldEffectClip),
                StartTime = clip.StartTime,
                Duration = duration,
                Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
            });
        }

        foreach (UnityHideHudClip clip in data.HideHudClips.OrderBy(c => c.StartTime))
        {
            int duration = Math.Max(0, clip.Duration);
            HideUserInterfaceClip payload = new()
            {
                Id = clip.Id,
                TrackId = clip.TrackId,
                IsActive = clip.IsActive ? 1 : 0,
                StartTime = clip.StartTime,
                Duration = clip.Duration,
                EventType = clip.EventType,
                CustomParam = clip.CustomParam
            };

            document.Events.Add(new TimelineEvent
            {
                Id = clip.Id,
                Type = nameof(HideUserInterfaceClip),
                StartTime = clip.StartTime,
                Duration = duration,
                Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
            });
        }

        return document;
    }

    private static (
        List<CoachTimelineDocument> HandTracking,
        List<CoachTimelineDocument> FullBodyTracking,
        Dictionary<string, CoachMoveDefinition> HandMoves,
        Dictionary<string, CoachMoveDefinition> FullBodyMoves) BuildCoachTimelines(IEnumerable<UnityMotionClip> clips)
    {
        Dictionary<int, CoachTimelineDocument> handTimelines = new();
        Dictionary<int, CoachTimelineDocument> fullBodyTimelines = new();
        Dictionary<string, CoachMoveDefinition> handMoves = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CoachMoveDefinition> fullBodyMoves = new(StringComparer.OrdinalIgnoreCase);

        foreach (UnityMotionClip clip in clips.OrderBy(c => c.StartTime))
        {
            bool isFullBody = clip.MoveType == 1;
            Dictionary<int, CoachTimelineDocument> timelines = isFullBody ? fullBodyTimelines : handTimelines;
            Dictionary<string, CoachMoveDefinition> moveCatalog = isFullBody ? fullBodyMoves : handMoves;

            if (!timelines.TryGetValue(clip.CoachId, out CoachTimelineDocument? doc))
            {
                doc = new CoachTimelineDocument { CoachId = clip.CoachId };
                timelines[clip.CoachId] = doc;
            }

            if (clip.TrackId != 0)
            {
                if (doc.TrackId == null)
                {
                    doc.TrackId = clip.TrackId;
                }
                else if (doc.TrackId != clip.TrackId)
                {
                    doc.TrackId = null;
                }
            }

            int duration = Math.Max(0, clip.Duration);
            string moveId = (clip.MoveName ?? string.Empty).ToLowerInvariant();

            CoachTimelineClip timelineClip = new()
            {
                Id = clip.Id,
                StartTime = clip.StartTime,
                MoveId = moveId,
                IsGoldMove = clip.IsGoldMove
            };

            doc.Clips.Add(timelineClip);

            AddOrUpdateMoveDefinition(
                moveCatalog,
                moveId,
                duration,
                isFullBody ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking);
        }

        return (
            handTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            fullBodyTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            handMoves,
            fullBodyMoves);
    }

    private static void AddOrUpdateMoveDefinition(
        Dictionary<string, CoachMoveDefinition> catalog,
        string moveId,
        int duration,
        CoachMoveType moveType)
    {
        if (!catalog.TryGetValue(moveId, out CoachMoveDefinition? definition))
        {
            catalog[moveId] = new CoachMoveDefinition
            {
                Duration = duration,
                MoveType = moveType
            };
            return;
        }
        else
        {
            // If anything is different, throw exception
            if (definition.MoveType != moveType)
                throw new InvalidDataException($"Inconsistent move type for coach move '{moveId}'.");
            if (definition.Duration != duration)
                throw new InvalidDataException($"Inconsistent duration for coach move '{moveId}'.");
        }

        if (duration > 0)
            definition.Duration = duration;

        definition.MoveType = moveType;
    }

    private static string LocateMapPackageBundle(string mapRoot)
    {
        string folder = Path.Combine(mapRoot, "MapPackage");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Unity server export missing MapPackage folder at '{folder}'.");

        string[] candidates = Directory.GetFiles(folder, "*.bundle", SearchOption.TopDirectoryOnly);
        if (candidates.Length == 0)
            candidates = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);

        string? bundle = candidates.OrderBy(f => f).FirstOrDefault();
        if (bundle == null)
            throw new FileNotFoundException($"No MapPackage bundle found inside '{folder}'.");

        return bundle;
    }

    private static void AddSingleFileAsset(
        IntermediateAssetCatalog catalog,
        string folder,
        string searchPattern,
        string role,
        bool required = true,
        bool allowFallback = false,
        string? explicitPath = null)
    {
        string? file = explicitPath ?? TryFirstFile(folder, searchPattern, allowFallback);
        if (file == null || !File.Exists(file))
            return;

        IntermediateAsset asset = catalog.Add(role);
        asset.SourcePath = file;
        asset.SizeBytes = new FileInfo(file).Length;
        asset.Required = required;
    }

    private static string? TryFirstFile(string folder, string searchPattern, bool allowFallback)
    {
        if (!Directory.Exists(folder))
            return null;

        string? match = Directory.EnumerateFiles(folder, searchPattern).OrderBy(f => f).FirstOrDefault();
        if (match != null)
            return match;

        if (!allowFallback)
            return null;

        return Directory.EnumerateFiles(folder).OrderBy(f => f).FirstOrDefault();
    }

    private static double ComputeSongStartOffset(Structure structure)
    {
        if (structure.markers == null || structure.markers.Length == 0)
            return 0;

        int index = Math.Clamp(Math.Abs(structure.startBeat), 0, structure.markers.Length - 1);
        double time = structure.markers[index] / 48d / 1000d;
        return structure.startBeat > 0 ? -time : time;
    }

    private static double CalculateMapLengthSeconds(Structure structure)
    {
        if (structure?.markers == null || structure.markers.Length == 0)
            return 0;

        int startIndex = Math.Clamp(Math.Abs(structure.startBeat), 0, structure.markers.Length - 1);
        int endIndex = Math.Clamp(structure.markers.Length - 1, 0, structure.markers.Length - 1);

        double startTime = structure.markers[startIndex] / 48d / 1000d;
        double endTime = structure.markers[endIndex] / 48d / 1000d;
        return Math.Max(0, endTime - startTime);
    }

    private static IEnumerable<AssetTypeValueField> Enumerate(AssetTypeValueField? arrayField)
    {
        if (arrayField == null || arrayField.IsDummy)
            yield break;

        foreach (AssetTypeValueField child in arrayField.Children)
            yield return child;
    }

    private static T[] ReadArray<T>(AssetTypeValueField? arrayField, Func<AssetTypeValueField, T> selector)
    {
        if (arrayField == null || arrayField.IsDummy)
            return Array.Empty<T>();

        T[] result = new T[arrayField.Children.Count];
        for (int i = 0; i < arrayField.Children.Count; i++)
            result[i] = selector(arrayField.Children[i]);
        return result;
    }

    private sealed record UnityMapPackageData(
        string MapPackagePath,
        Structure Structure,
        List<UnityKaraokeClip> KaraokeClips,
        List<UnityMotionClip> MotionClips,
        List<UnityPictoClip> PictogramClips,
        List<UnityGoldEffectClip> GoldEffectClips,
        List<UnityHideHudClip> HideHudClips);

    private sealed record UnityKaraokeClip(
        int StartTime,
        int Duration,
        string Lyrics,
        float Pitch,
        bool IsEndOfLine,
        int ContentType,
        long Id,
        long TrackId,
        bool IsActive,
        int SemitoneTolerance,
        int StartTimeTolerance,
        int EndTimeTolerance);

    private sealed record UnityMotionClip(
        int StartTime,
        int Duration,
        string MoveName,
        int CoachId,
        bool IsGoldMove,
        int MoveType,
        long Id,
        long TrackId,
        bool IsActive,
        string Color);

    private sealed record UnityPictoClip(
        int StartTime,
        int Duration,
        string PictoName,
        int CoachCount,
        long Id,
        long TrackId,
        bool IsActive);

    private sealed record UnityGoldEffectClip(
        int StartTime,
        int Duration,
        int GoldEffectType,
        long Id,
        long TrackId,
        bool IsActive);

    private sealed record UnityHideHudClip(
        int StartTime,
        int Duration,
        long Id,
        long TrackId,
        bool IsActive,
        int EventType,
        string CustomParam);

    private sealed class UnitySongInfo
    {
        [JsonPropertyName("songID")]
        public Guid SongId { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonPropertyName("coachCount")]
        public int CoachCount { get; set; }

        [JsonPropertyName("coachNames")]
        public string[]? CoachNames { get; set; }

        [JsonPropertyName("coachNamesLocIds")]
        [JsonConverter(typeof(FlexibleStringArrayConverter))]
        public string[]? CoachNamesLocIds { get; set; }

        [JsonPropertyName("credits")]
        public string? Credits { get; set; }

        [JsonPropertyName("danceVersionLocId")]
        public int? DanceVersionLocId { get; set; }

        [JsonPropertyName("difficulty")]
        public uint Difficulty { get; set; }

        [JsonPropertyName("doubleScoringType")]
        public string? DoubleScoringType { get; set; }

        [JsonPropertyName("hasSongTitleInCover")]
        public bool HasSongTitleInCover { get; set; }

        [JsonPropertyName("lyricsColor")]
        public string? LyricsColor { get; set; }

        [JsonPropertyName("mapLength")]
        public double MapLength { get; set; }

        [JsonPropertyName("mapName")]
        public string? MapName { get; set; }

        [JsonPropertyName("originalJDVersion")]
        public uint OriginalJdVersion { get; set; }

        [JsonPropertyName("parentMapName")]
        public string? ParentMapName { get; set; }

        [JsonPropertyName("sweatDifficulty")]
        public uint SweatDifficulty { get; set; }

        [JsonPropertyName("tagIds")]
        public string[]? TagIds { get; set; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class FlexibleStringArrayConverter : JsonConverter<string[]?>
    {
        public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("coachNamesLocIds must be an array.");

            List<string> values = new();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                switch (reader.TokenType)
                {
                    case JsonTokenType.Null:
                        values.Add(string.Empty);
                        break;
                    case JsonTokenType.String:
                        values.Add(reader.GetString() ?? string.Empty);
                        break;
                    case JsonTokenType.Number:
                        values.Add(reader.TryGetInt64(out long number)
                            ? number.ToString(CultureInfo.InvariantCulture)
                            : reader.GetDouble().ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                        {
                            values.Add(doc.RootElement.ToString());
                        }

                        break;
                }
            }

            return values.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            foreach (string item in value)
                writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }
}
