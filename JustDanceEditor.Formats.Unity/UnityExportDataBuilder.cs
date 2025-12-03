using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Globalization;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity;

public static class UnityExportDataBuilder
{
    private static readonly JsonSerializerOptions EventSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static UnityExportData Create(IntermediateSongPackage package)
    {
        IntermediateMetadata metadata = package.Metadata;
        metadata.Validate();

        UnityTrackStructure structure = TimelineStructureFactory.Build(package.TimelineStructure ?? new());

        UnityExportMetadata exportMetadata = new()
        {
            SongId = metadata.SongId,
            MapName = metadata.MapName ?? string.Empty,
            ParentMapName = metadata.ParentMapName ?? string.Empty,
            Title = metadata.Title ?? string.Empty,
            Artist = metadata.Artist ?? string.Empty,
            Credits = metadata.Credits ?? string.Empty,
            LyricsColor = string.IsNullOrWhiteSpace(metadata.LyricsColor) ? "#FFFFFFFF" : metadata.LyricsColor!,
            MapLengthSeconds = metadata.MapLengthSeconds,
            EngineVersion = metadata.EngineVersion,
            OriginalJdVersion = metadata.OriginalJdVersion,
            CoachCount = metadata.CoachCount,
            Difficulty = metadata.Difficulty,
            SweatDifficulty = metadata.SweatDifficulty,
            Tags = metadata.Tags?.ToArray() ?? Array.Empty<string>(),
            HasSongTitleInCover = metadata.HasSongTitleInCover
        };

        UnityClipAssembler assembler = new(package);
        IReadOnlyList<UnityKaraokeClip> karaoke = assembler.BuildKaraokeClips();
        IReadOnlyList<UnityPictogramClip> pictos = assembler.BuildPictogramClips();
        IReadOnlyList<UnityMotionClip> motions = assembler.BuildMotionClips();
        IReadOnlyList<UnityGoldEffectClip> goldEffects = assembler.BuildGoldEffectClips();
        IReadOnlyList<UnityHideHudClip> hideHud = assembler.BuildHideHudClips();
        IReadOnlyList<UnityGameplayEventClip> gameplayEvents = assembler.BuildGameplayEvents();
        IReadOnlyList<UnityVibrationClip> vibrations = assembler.BuildVibrationClips();

        string name = string.IsNullOrWhiteSpace(metadata.MapName) ? metadata.Title ?? string.Empty : metadata.MapName;
        if (string.IsNullOrWhiteSpace(name))
            name = "Song";

        return new UnityExportData(
            name,
            exportMetadata,
            structure,
            karaoke,
            pictos,
            motions,
            goldEffects,
            hideHud,
            gameplayEvents,
            vibrations);
    }

    private sealed class UnityClipAssembler
    {
        private readonly IntermediateSongPackage _package;
        private long _nextClipId;

        public UnityClipAssembler(IntermediateSongPackage package)
        {
            _package = package;
            _nextClipId = Math.Max(1_000_000, DetermineMaxId(package) + 1);
        }

        public IReadOnlyList<UnityKaraokeClip> BuildKaraokeClips()
        {
            List<UnityKaraokeClip> clips = [];
            if (_package.Lyrics?.Clips == null)
                return clips;

            foreach (var clip in _package.Lyrics.Clips.OrderBy(c => c.StartTime))
            {
                long id = AllocateClipId(clip.Id);

                UnityKaraokeClip karaoke = new()
                {
                    Id = id,
                    TrackId = id,
                    IsActive = 1,
                    StartTime = clip.StartTime,
                    Duration = clip.Duration,
                    Lyrics = clip.Lyrics,
                    Pitch = clip.Pitch,
                    IsEndOfLine = clip.IsEndOfLine ? 1 : 0,
                    ContentType = clip.ContentType
                };

                if (clip.Tolerances != null)
                {
                    karaoke.StartTimeTolerance = clip.Tolerances.StartTimeTolerance;
                    karaoke.EndTimeTolerance = clip.Tolerances.EndTimeTolerance;
                    karaoke.SemitoneTolerance = (float)clip.Tolerances.SemitoneTolerance;
                }

                clips.Add(karaoke);
            }

            return clips;
        }

        public IReadOnlyList<UnityPictogramClip> BuildPictogramClips()
        {
            List<UnityPictogramClip> clips = [];
            if (_package.Pictograms?.Entries == null)
                return clips;

            foreach (var entry in _package.Pictograms.Entries.OrderBy(e => e.StartTime))
            {
                long id = AllocateClipId(entry.Id);

                string pictoName = string.IsNullOrWhiteSpace(entry.PictogramId)
                    ? $"picto_{id}"
                    : entry.PictogramId;

                clips.Add(new UnityPictogramClip
                {
                    Id = id,
                    TrackId = id,
                    IsActive = 1,
                    StartTime = entry.StartTime,
                    Duration = entry.Duration,
                    PictoPath = pictoName + ".png",
                    CoachCount = entry.CoachCount
                });
            }

            return clips;
        }

        public IReadOnlyList<UnityMotionClip> BuildMotionClips()
        {
            List<UnityMotionClip> clips = [];
            if (_package.CoachTimelines == null && _package.FullBodyCoachTimelines == null)
                return clips;

            IEnumerable<(CoachTimelineDocument Timeline, CoachMoveType MoveType)> timelines = EnumerateCoachTimelines()
                .OrderBy(entry => entry.Timeline.CoachId);

            foreach ((CoachTimelineDocument Timeline, CoachMoveType MoveType) in timelines)
            {
                foreach (CoachTimelineClip clip in Timeline.Clips.OrderBy(c => c.StartTime))
                {
                    long id = AllocateClipId(clip.Id);
                    CoachMoveDefinition? definition = FindMoveDefinition(clip.MoveId, MoveType);

                    string moveName = string.IsNullOrWhiteSpace(clip.MoveId)
                        ? $"move_{Timeline.CoachId}"
                        : clip.MoveId.ToLowerInvariant();

                    if (definition == null)
                    {
                        Logging.Logger.Log($"Move definition not found for move ID '{clip.MoveId}' (Coach ID: {Timeline.CoachId}). Using default duration.", Logging.LogLevel.Warning);
                    }

                    clips.Add(new UnityMotionClip
                    {
                        Id = id,
                        TrackId = Timeline.TrackId ?? id,
                        IsActive = 1,
                        StartTime = clip.StartTime,
                        Duration = definition?.Duration ?? 48,
                        MoveName = moveName,
                        GoldMove = clip.IsGoldMove ? 1 : 0,
                        CoachId = Timeline.CoachId,
                        MoveType = MoveType == CoachMoveType.FullBodyTracking ? 1 : 0
                    });
                }
            }

            return clips;
        }

        private IEnumerable<(CoachTimelineDocument Timeline, CoachMoveType MoveType)> EnumerateCoachTimelines()
        {
            if (_package.CoachTimelines != null)
            {
                foreach (CoachTimelineDocument timeline in _package.CoachTimelines)
                    yield return (timeline, CoachMoveType.HandTracking);
            }

            if (_package.FullBodyCoachTimelines != null)
            {
                foreach (CoachTimelineDocument timeline in _package.FullBodyCoachTimelines)
                    yield return (timeline, CoachMoveType.FullBodyTracking);
            }
        }

        private CoachMoveDefinition? FindMoveDefinition(string moveId, CoachMoveType preferredType)
        {
            if (string.IsNullOrWhiteSpace(moveId))
                return null;

            CoachMoveDefinition? definition = TryLookupMoveDefinition(moveId, preferredType);
            if (definition != null)
                return definition;

            CoachMoveType alternate = preferredType == CoachMoveType.FullBodyTracking
                ? CoachMoveType.HandTracking
                : CoachMoveType.FullBodyTracking;
            return TryLookupMoveDefinition(moveId, alternate);
        }

        private CoachMoveDefinition? TryLookupMoveDefinition(string moveId, CoachMoveType moveType)
        {
            Dictionary<string, CoachMoveDefinition>? catalog = moveType == CoachMoveType.FullBodyTracking
                ? _package.FullBodyCoachMoves
                : _package.HandCoachMoves;

            if (catalog != null && catalog.TryGetValue(moveId, out CoachMoveDefinition? definition))
                return definition;

            return null;
        }

        public IReadOnlyList<UnityGoldEffectClip> BuildGoldEffectClips()
        {
            return BuildEventClips<GoldEffectClipPayload, UnityGoldEffectClip>(nameof(UnityGoldEffectClip), payload => new UnityGoldEffectClip
            {
                EffectType = payload?.EffectType ?? 0
            });
        }

        public IReadOnlyList<UnityHideHudClip> BuildHideHudClips()
        {
            return BuildEventClips<HideHudPayload, UnityHideHudClip>(nameof(UnityHideHudClip), _ => new UnityHideHudClip());
        }

        public IReadOnlyList<UnityGameplayEventClip> BuildGameplayEvents()
        {
            return BuildEventClips<GameplayEventPayload, UnityGameplayEventClip>(nameof(UnityGameplayEventClip), payload => new UnityGameplayEventClip
            {
                Payload = payload?.EventName ?? string.Empty
            });
        }

        public IReadOnlyList<UnityVibrationClip> BuildVibrationClips()
        {
            return BuildEventClips<VibrationPayload, UnityVibrationClip>(nameof(UnityVibrationClip), _ => new UnityVibrationClip());
        }

        private IReadOnlyList<TClip> BuildEventClips<TPayload, TClip>(string typeName, Func<TPayload?, TClip> factory)
            where TClip : UnityClip, new()
        {
            List<TClip> clips = [];
            if (_package.Events?.Events == null)
                return clips;

            foreach (TimelineEvent evt in _package.Events.Events.OrderBy(e => e.StartTime))
            {
                if (!string.Equals(evt.Type, typeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                TPayload? payload = Deserialize<TPayload>(evt.Payload);
                TClip clip = factory(payload);

                long id = AllocateClipId(evt.Id);

                clip.Id = id;
                clip.TrackId = clip.TrackId != 0 ? clip.TrackId : id;
                clip.IsActive = clip.IsActive == 0 ? 1 : clip.IsActive;
                clip.StartTime = clip.StartTime;
                clip.Duration = clip.Duration;
                clips.Add(clip);
            }

            return clips;
        }

        private static T? Deserialize<T>(JsonElement element)
        {
            try
            {
                return element.ValueKind == JsonValueKind.Undefined
                    ? default
                    : element.Deserialize<T>(EventSerializerOptions);
            }
            catch
            {
                return default;
            }
        }

        private long AllocateClipId(long requested)
        {
            if (requested > 0)
                return requested;
            return _nextClipId++;
        }

        private static long DetermineMaxId(IntermediateSongPackage package)
        {
            long max = 0;

            static long MaxId<T>(IEnumerable<T> source, Func<T, long> selector)
            {
                long current = 0;
                foreach (T item in source)
                    current = Math.Max(current, selector(item));
                return current;
            }

            if (package.Lyrics?.Clips != null)
                max = Math.Max(max, MaxId(package.Lyrics.Clips, c => c.Id));
            if (package.Pictograms?.Entries != null)
                max = Math.Max(max, MaxId(package.Pictograms.Entries, c => c.Id));
            if (package.CoachTimelines != null)
                max = Math.Max(max, MaxId(package.CoachTimelines.SelectMany(t => t.Clips), c => c.Id));
            if (package.FullBodyCoachTimelines != null)
                max = Math.Max(max, MaxId(package.FullBodyCoachTimelines.SelectMany(t => t.Clips), c => c.Id));
            if (package.Events?.Events != null)
                max = Math.Max(max, MaxId(package.Events.Events, e => e.Id));

            return max;
        }

        private sealed class GoldEffectClipPayload
        {
            public int EffectType { get; set; }
        }

        private sealed class HideHudPayload
        {
        }

        private sealed class GameplayEventPayload
        {
            public string EventName { get; set; } = string.Empty;
        }

        private sealed class VibrationPayload
        {
        }
    }

    private static class TimelineStructureFactory
    {
        public static UnityTrackStructure Build(TimelineStructureDocument document)
        {
            UnityTrackStructure structure = new()
            {
                startBeat = document.Markers.Count > 0 ? document.Markers.Min(m => m.BeatIndex) : 0,
                endBeat = document.Markers.Count > 0 ? document.Markers.Max(m => m.BeatIndex) : 0,
                videoStartTime = document.VideoStartOffset,
                previewEntry = (int)Math.Round(document.PreviewEntryBeat),
                previewLoopStart = (int)Math.Round(document.PreviewLoopStartBeat),
                previewLoopEnd = (int)Math.Round(document.PreviewLoopEndBeat),
                markers = BuildMarkers(document),
                signatures = document.Signatures
                    .Select(s => new UnitySignature { marker = (float)s.StartBeat, beats = s.Numerator })
                    .ToArray(),
                sections = document.Sections
                    .Select(s => new UnitySection
                    {
                        marker = (float)s.StartBeat,
                        sectionType = ParseInt(s.SectionType),
                        comment = s.Comment ?? string.Empty
                    })
                    .ToArray()
            };

            if (document.FadeIn != null)
            {
                structure.useFadeStartBeat = true;
                structure.fadeStartBeat = (int)Math.Round(document.FadeIn.StartBeat);
                structure.fadeInType = string.Equals(document.FadeIn.CurveType, "linear", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }

            if (document.FadeOut != null)
            {
                structure.useFadeEndBeat = true;
                structure.fadeEndBeat = (int)Math.Round(document.FadeOut.StartBeat);
                structure.fadeOutType = string.Equals(document.FadeOut.CurveType, "linear", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }
            else if (document.FadeIn != null)
            {
                structure.fadeEndBeat = (int)Math.Round(document.FadeIn.StartBeat + document.FadeIn.Duration);
            }
            else if (structure.markers.Length > 0)
            {
                structure.fadeEndBeat = structure.endBeat;
            }

            return structure;
        }

        private static int[] BuildMarkers(TimelineStructureDocument document)
        {
            if (document.Markers.Count == 0)
            {
                double ticks = Math.Max(1, document.TimeBaseMsPerBeat);
                return [(int)Math.Round(-ticks * 48d), 0];
            }

            List<TimelineMarker> ordered = document.Markers
                .OrderBy(m => m.BeatIndex)
                .ToList();

            int[] markers = new int[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
                markers[i] = (int)Math.Round(ordered[i].TimeMs * 48d);
            return markers;
        }

        private static int ParseInt(string? value)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
    }
}
