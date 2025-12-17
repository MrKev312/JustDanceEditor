using AssetsTools.NET;
using AssetsTools.NET.Extra;

using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Models;

using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Builders;

internal static partial class UnityServerIntermediateBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static IntermediateSongPackage FromServerExport(string mapRoot)
    {
        if (string.IsNullOrWhiteSpace(mapRoot))
            throw new ArgumentException("Map root cannot be empty.", nameof(mapRoot));

        string songInfoPath = Path.Combine(mapRoot, "SongInfo.json");
        if (!File.Exists(songInfoPath))
            throw new FileNotFoundException($"Unity server export missing SongInfo.json at '{songInfoPath}'.");

        // 1. Load basic info
        ServerSongJSON songInfo = LoadSongInfo(songInfoPath);

        // 2. Load Bundle and find MonoBehaviours
        string mapPackagePath = LocateMapPackageBundle(mapRoot);
        AssetsManager manager = new();

        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(mapPackagePath, true);
            AssetsFileInstance assetsFile = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            AssetsFile assets = assetsFile.file;
            assets.GenerateQuickLookup();

            (AssetTypeValueField musicTrackBase, AssetTypeValueField mapBehaviourBase) = FindRequiredMonoBehaviours(manager, assetsFile);

            // 3. Parse Structure
            Structure structure = ParseStructure(musicTrackBase);
            TimelineMath timelineMath = new(structure);

            // 4. Parse Motion/Coach Data directly into JDI models
            (
                List<MoveTimeline> handTimelines,
                List<MoveTimeline> fullBodyTimelines,
                Dictionary<string, CoachMoveDefinition> handMoves,
                Dictionary<string, CoachMoveDefinition> fullBodyMoves
            ) = BuildCoachTimelinesAndMoves(mapBehaviourBase);

            // 5. Build final package
            IntermediateSongPackage package = new()
            {
                Metadata = (IntermediateMetadata)songInfo,
                TimelineStructure = BuildTimelineStructure(structure, timelineMath),

                Lyrics = BuildLyricsDocument(mapBehaviourBase),
                Pictograms = BuildPictogramDocument(mapBehaviourBase),
                GoldEffects = BuildGoldEffectDocument(mapBehaviourBase),
                HideUserInterface = BuildHideUserInterfaceDocument(mapBehaviourBase),

                CoachTimelines = handTimelines,
                FullBodyCoachTimelines = fullBodyTimelines,
                HandCoachMoves = handMoves,
                FullBodyCoachMoves = fullBodyMoves
            };

            package.Metadata.Validate();
            return package;
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static (AssetTypeValueField MusicTrack, AssetTypeValueField MapBehaviour) FindRequiredMonoBehaviours(AssetsManager manager, AssetsFileInstance assetsFile)
    {
        AssetFileInfo[] monoInfos = [.. assetsFile.file.AssetInfos.Where(x => x.TypeId == (int)AssetClassID.MonoBehaviour)];
        if (monoInfos.Length < 2)
            throw new InvalidDataException("MapPackage bundle does not contain the required MonoBehaviours.");

        AssetTypeValueField? musicTrack = null;
        AssetTypeValueField? mapBehaviour = null;

        foreach (AssetFileInfo info in monoInfos)
        {
            AssetTypeValueField field = manager.GetBaseField(assetsFile, info);
            string name = field["m_Name"].AsString;

            if (string.IsNullOrWhiteSpace(name) && musicTrack == null)
            {
                musicTrack = field;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                mapBehaviour ??= field;
            }

            if (musicTrack != null && mapBehaviour != null)
                break;
        }

        if (musicTrack == null || mapBehaviour == null)
            throw new InvalidDataException("Could not locate MusicTrack and MapBehaviour MonoBehaviours inside the MapPackage.");

        return (musicTrack, mapBehaviour);
    }

    private static Timeline<KaraokeClip> BuildLyricsDocument(AssetTypeValueField mapBehaviour)
    {
        Timeline<KaraokeClip> document = new();
        AssetTypeValueField clipsArray = mapBehaviour["KaraokeData"]["Clips"]["Array"];

        // Helper to safely enumerate
        foreach (AssetTypeValueField entry in Enumerate(clipsArray))
        {
            // Note: Structure in bundle is usually Container -> KaraokeClip
            AssetTypeValueField clipField = entry["KaraokeClip"];

            KaraokeClip jdiClip = new()
            {
                Id = clipField["Id"].AsLong,
                StartTime = clipField["StartTime"].AsInt,
                Duration = clipField["Duration"].AsInt,
                Lyrics = clipField["Lyrics"].AsString,
                Pitch = clipField["Pitch"].AsFloat,
                IsEndOfLine = clipField["IsEndOfLine"].AsUInt > 0,
                ContentType = clipField["ContentType"].AsInt,
                Tolerances = BuildTolerance(clipField)
            };

            document.Clips.Add(jdiClip);
        }

        // Sort by start time just to be safe
        document.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return document;
    }

    private static KaraokeTolerance? BuildTolerance(AssetTypeValueField clipField)
    {
        int startTol = clipField["StartTimeTolerance"].IsDummy ? 0 : clipField["StartTimeTolerance"].AsInt;
        int endTol = clipField["EndTimeTolerance"].IsDummy ? 0 : clipField["EndTimeTolerance"].AsInt;
        float semiTol = clipField["SemitoneTolerance"].IsDummy ? 0 : clipField["SemitoneTolerance"].AsFloat;

        if (startTol == 0 && endTol == 0 && semiTol == 0)
            return null;

        return new KaraokeTolerance
        {
            StartTimeTolerance = Math.Abs(startTol),
            EndTimeTolerance = Math.Abs(endTol),
            SemitoneTolerance = semiTol
        };
    }

    private static Timeline<PictogramClip> BuildPictogramDocument(AssetTypeValueField mapBehaviour)
    {
        Timeline<PictogramClip> document = new();
        AssetTypeValueField clipsArray = mapBehaviour["DanceData"]["PictoClips"]["Array"];

        foreach (AssetTypeValueField entry in Enumerate(clipsArray))
        {
            document.Clips.Add(new PictogramClip
            {
                Id = entry["Id"].AsLong,
                StartTime = entry["StartTime"].AsInt,
                Duration = Math.Max(0, entry["Duration"].AsInt),
                PictogramId = entry["PictoPath"].AsString,
                CoachCount = (int)entry["CoachCount"].AsUInt
            });
        }

        document.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return document;
    }

    private static Timeline<GoldEffectClip> BuildGoldEffectDocument(AssetTypeValueField mapBehaviour)
    {
        Timeline<GoldEffectClip> document = new();
        AssetTypeValueField clipsArray = mapBehaviour["DanceData"]["GoldEffectClips"]["Array"];

        foreach (AssetTypeValueField entry in Enumerate(clipsArray))
        {
            document.Clips.Add(new GoldEffectClip
            {
                Id = entry["Id"].AsLong,
                TrackId = entry["TrackId"].AsLong,
                IsActive = entry["IsActive"].AsUInt > 0,
                StartTime = entry["StartTime"].AsInt,
                Duration = Math.Max(0, entry["Duration"].AsInt),
                EffectType = entry["GoldEffectType"].AsInt
            });
        }

        document.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return document;
    }

    private static Timeline<HideUserInterfaceClip> BuildHideUserInterfaceDocument(AssetTypeValueField mapBehaviour)
    {
        Timeline<HideUserInterfaceClip> document = new();
        AssetTypeValueField clipsArray = mapBehaviour["DanceData"]["HideHudClips"]["Array"];

        foreach (AssetTypeValueField entry in Enumerate(clipsArray))
        {
            document.Clips.Add(new HideUserInterfaceClip
            {
                IsActive = entry["IsActive"].AsUInt > 0,
                StartTime = entry["StartTime"].AsInt,
                Duration = Math.Max(0, entry["Duration"].AsInt),
            });
        }

        document.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return document;
    }

    private static (
        List<MoveTimeline> HandTracking,
        List<MoveTimeline> FullBodyTracking,
        Dictionary<string, CoachMoveDefinition> HandMoves,
        Dictionary<string, CoachMoveDefinition> FullBodyMoves) BuildCoachTimelinesAndMoves(AssetTypeValueField mapBehaviour)
    {
        Dictionary<int, MoveTimeline> handTimelines = [];
        Dictionary<int, MoveTimeline> fullBodyTimelines = [];
        Dictionary<string, CoachMoveDefinition> handMoves = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CoachMoveDefinition> fullBodyMoves = new(StringComparer.OrdinalIgnoreCase);

        AssetTypeValueField clipsArray = mapBehaviour["DanceData"]["MotionClips"]["Array"];

        // Iterate raw AssetsTools fields
        foreach (AssetTypeValueField entry in Enumerate(clipsArray))
        {
            int startTime = entry["StartTime"].AsInt;

            // Determine type
            // 0 = Hand, 1 = FullBody
            int moveTypeRaw = entry["MoveType"].AsInt;
            bool isFullBody = moveTypeRaw == 1;

            Dictionary<int, MoveTimeline> timelines = isFullBody ? fullBodyTimelines : handTimelines;
            Dictionary<string, CoachMoveDefinition> moveCatalog = isFullBody ? fullBodyMoves : handMoves;

            // Get Coach Document
            int coachId = entry["CoachId"].AsInt;
            if (!timelines.TryGetValue(coachId, out MoveTimeline? doc))
            {
                doc = new MoveTimeline { CoachId = coachId };
                timelines[coachId] = doc;
            }

            // Track ID Logic
            long trackId = entry["TrackId"].AsLong;
            doc.TrackId = trackId;

            // Create Clip
            int duration = Math.Max(0, entry["Duration"].AsInt);
            string moveName = entry["MoveName"].AsString;
            string moveId = (moveName ?? string.Empty).ToLowerInvariant();

            MoveClip timelineClip = new()
            {
                Id = entry["Id"].AsLong,
                StartTime = startTime,
                MoveId = moveId,
                IsGoldMove = entry["GoldMove"].AsUInt > 0
            };

            doc.Clips.Add(timelineClip);

            // Update Move Definition
            AddOrUpdateMoveDefinition(
                moveCatalog,
                moveId,
                duration,
                isFullBody ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking);
        }

        // Sort clips within timelines
        foreach (MoveTimeline t in handTimelines.Values)
            t.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        foreach (MoveTimeline t in fullBodyTimelines.Values)
            t.Clips.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        return (
            handTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            fullBodyTimelines.Values.OrderBy(t => t.CoachId).ToList(),
            handMoves,
            fullBodyMoves);
    }

    private static Structure ParseStructure(AssetTypeValueField musicTrackBase)
    {
        AssetTypeValueField structureField = musicTrackBase["m_structure"]["MusicTrackStructure"];

        static int ReadInt(AssetTypeValueField parent, string name) => parent[name].IsDummy ? 0 : parent[name].AsInt;
        static double ReadDouble(AssetTypeValueField parent, string name) => parent[name].IsDummy ? 0 : parent[name].AsDouble;

        Structure structure = new()
        {
            startBeat = ReadInt(structureField, "startBeat"),
            endBeat = ReadInt(structureField, "endBeat"),
            videoStartTime = (float)ReadDouble(structureField, "videoStartTime"),
            previewEntry = (int)Math.Round(ReadDouble(structureField, "previewEntry")),
            previewLoopStart = (int)Math.Round(ReadDouble(structureField, "previewLoopStart")),
            previewLoopEnd = (int)Math.Round(ReadDouble(structureField, "previewLoopEnd")),
            previewDuration = (int)Math.Round(ReadDouble(structureField, "previewDuration")),
            markers = ReadArray(structureField["markers"]["Array"], field => (int)field["VAL"].AsLong),
            signatures = ReadArray(structureField["signatures"]["Array"], field => new Signature
            {
                beats = field["MusicSignature"]["beats"].AsInt,
                marker = (float)field["MusicSignature"]["marker"].AsDouble
            }),

            sections = ReadArray(structureField["sections"]["Array"], field => new Section
            {
                sectionType = field["MusicSection"]["sectionType"].AsInt,
                marker = (float)field["MusicSection"]["marker"].AsDouble,
                comment = field["MusicSection"]["comment"].AsString
            })
        };

        if (structure.previewDuration == 0)
            structure.previewDuration = 30;

        return structure;
    }

    private static TimelineStructureDocument BuildTimelineStructure(Structure structure, TimelineMath timelineMath)
    {
        TimelineStructureDocument document = new()
        {
            TimeBaseMsPerBeat = timelineMath.EstimateMsPerBeat(),
            StartBeat = structure.startBeat,
            EndBeat = structure.endBeat,
            VideoStartOffset = structure.videoStartTime,
            PreviewEntryBeat = structure.previewEntry,
            PreviewLoopStartBeat = structure.previewLoopStart,
            PreviewLoopEndBeat = structure.previewLoopEnd,
            PrevewDuration = structure.previewDuration,
            Markers = [.. structure.markers]
        };

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
                    Beats = signature.beats,
                    Marker = signature.marker,
                    Comment = signature.comment
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
                    SectionType = section.sectionType,
                    Comment = section.comment
                });
            }
        }

        return document;
    }

    private static ServerSongJSON LoadSongInfo(string songInfoPath)
    {
        string json = File.ReadAllText(songInfoPath);
        ServerSongJSON info = JsonSerializer.Deserialize<ServerSongJSON>(json, JsonOptions) ?? throw new InvalidDataException($"Failed to deserialize SongInfo.json located at '{songInfoPath}'.");
        return info;
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
            return [];

        T[] result = new T[arrayField.Children.Count];
        for (int i = 0; i < arrayField.Children.Count; i++)
            result[i] = selector(arrayField.Children[i]);
        return result;
    }
}