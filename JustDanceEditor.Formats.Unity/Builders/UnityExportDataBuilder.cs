using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Models;

using System.Globalization;

namespace JustDanceEditor.Formats.Unity.Builders;

public static class UnityExportDataBuilder
{
    public static UnityExportData Create(IntermediateSongPackage package)
    {
        IntermediateMetadata metadata = package.Metadata;
        metadata.Validate();

        UnityTrackStructure structure = TimelineStructureFactory.Build(package.TimelineStructure ?? new());

        UnityExportMetadata exportMetadata = new()
        {
            SongId = metadata.SongId,
            Artist = metadata.Artist ?? string.Empty,
            CoachCount = metadata.CoachCount,
            CoachNamesLocIds = metadata.CoachNames?.ToArray() ?? [],
            Credits = metadata.Credits ?? string.Empty,
            DanceVersionLocId = 0,
            Difficulty = metadata.Difficulty,
            // Todo: DoubleScoringType: do we have gestures?
            HasSongTitleInCover = metadata.HasSongTitleInCover,
            LyricsColor = string.IsNullOrWhiteSpace(metadata.LyricsColor) ? "#FFFFFFFF" : metadata.LyricsColor!,
            MapLength = metadata.MapLengthSeconds,
            MapName = metadata.MapName,
            OriginalJdVersion = metadata.OriginalJdVersion,
            ParentMapName = metadata.ParentMapName,
            SweatDifficulty = metadata.SweatDifficulty,
            TagIds = [],
            Tags = metadata.Tags?.ToArray() ?? [],
            Title = metadata.Title ?? string.Empty,
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

    private sealed class UnityClipAssembler(IntermediateSongPackage package)
    {
        private long _nextClipId = Math.Max(1_000_000, DetermineMaxId(package) + 1);

        public IReadOnlyList<UnityKaraokeClip> BuildKaraokeClips()
        {
            List<UnityKaraokeClip> clips = [];
            if (package.Lyrics?.Clips == null)
                return clips;

            foreach (KaraokeClip? clip in package.Lyrics.Clips.OrderBy(c => c.StartTime))
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
            if (package.Pictograms?.Entries == null)
                return clips;

            foreach (PictogramEntry? entry in package.Pictograms.Entries.OrderBy(e => e.StartTime))
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
                    CoachCount = (uint)entry.CoachCount
                });
            }

            return clips;
        }

        public IReadOnlyList<UnityMotionClip> BuildMotionClips()
        {
            List<UnityMotionClip> clips = [];
            if (package.CoachTimelines == null && package.FullBodyCoachTimelines == null)
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
                        TrackId = Timeline.TrackId,
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
            if (package.CoachTimelines != null)
            {
                foreach (CoachTimelineDocument timeline in package.CoachTimelines)
                    yield return (timeline, CoachMoveType.HandTracking);
            }

            if (package.FullBodyCoachTimelines != null)
            {
                foreach (CoachTimelineDocument timeline in package.FullBodyCoachTimelines)
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
                ? package.FullBodyCoachMoves
                : package.HandCoachMoves;

            if (catalog != null && catalog.TryGetValue(moveId, out CoachMoveDefinition? definition))
                return definition;

            return null;
        }

        public IReadOnlyList<UnityGoldEffectClip> BuildGoldEffectClips()
        {
            List<UnityGoldEffectClip> clips = [];
            if (package.GoldEffects?.Clips == null)
                return clips;

            foreach (GoldEffectTimelineClip entry in package.GoldEffects.Clips.OrderBy(c => c.StartTime))
            {
                long id = AllocateClipId(entry.Id);

                clips.Add(new UnityGoldEffectClip
                {
                    Id = id,
                    TrackId = entry.TrackId,
                    IsActive = entry.IsActive ? 1 : 0,
                    StartTime = entry.StartTime,
                    Duration = entry.Duration,
                    EffectType = entry.EffectType
                });
            }

            return clips;
        }

        public IReadOnlyList<UnityHideHudClip> BuildHideHudClips()
        {
            List<UnityHideHudClip> clips = [];
            if (package.HideUserInterface?.Clips == null)
                return clips;

            foreach (HideUserInterfaceTimelineClip entry in package.HideUserInterface.Clips.OrderBy(c => c.StartTime))
            {
                long id = AllocateClipId(entry.Id);

                clips.Add(new UnityHideHudClip
                {
                    Id = id,
                    TrackId = entry.TrackId,
                    IsActive = entry.IsActive ? 1 : 0,
                    StartTime = entry.StartTime,
                    Duration = entry.Duration
                });
            }

            return clips;
        }

        public IReadOnlyList<UnityGameplayEventClip> BuildGameplayEvents()
        {
            List<UnityGameplayEventClip> clips = [];
            if (package.GameplayEvents?.Clips == null)
                return clips;

            foreach (GameplayEventTimelineClip entry in package.GameplayEvents.Clips.OrderBy(c => c.StartTime))
            {
                long id = AllocateClipId(entry.Id);

                clips.Add(new UnityGameplayEventClip
                {
                    Id = id,
                    TrackId = entry.TrackId,
                    IsActive = entry.IsActive ? 1 : 0,
                    StartTime = entry.StartTime,
                    Duration = entry.Duration,
                    Payload = entry.EventName ?? string.Empty
                });
            }

            return clips;
        }

        public IReadOnlyList<UnityVibrationClip> BuildVibrationClips()
        {
            List<UnityVibrationClip> clips = [];
            if (package.Vibrations?.Clips == null)
                return clips;

            foreach (VibrationTimelineClip entry in package.Vibrations.Clips.OrderBy(c => c.StartTime))
            {
                long id = AllocateClipId(entry.Id);

                clips.Add(new UnityVibrationClip
                {
                    Id = id,
                    TrackId = entry.TrackId,
                    IsActive = entry.IsActive ? 1 : 0,
                    StartTime = entry.StartTime,
                    Duration = entry.Duration
                });
            }

            return clips;
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
            if (package.GoldEffects?.Clips != null)
                max = Math.Max(max, MaxId(package.GoldEffects.Clips, c => c.Id));
            if (package.HideUserInterface?.Clips != null)
                max = Math.Max(max, MaxId(package.HideUserInterface.Clips, c => c.Id));
            if (package.GameplayEvents?.Clips != null)
                max = Math.Max(max, MaxId(package.GameplayEvents.Clips, c => c.Id));
            if (package.Vibrations?.Clips != null)
                max = Math.Max(max, MaxId(package.Vibrations.Clips, c => c.Id));

            return max;
        }
    }

    private static class TimelineStructureFactory
    {
        public static UnityTrackStructure Build(TimelineStructureDocument document)
        {
            UnityTrackStructure structure = new()
            {
                startBeat = document.StartBeat,
                endBeat = document.EndBeat,
                videoStartTime = document.VideoStartOffset,
                previewEntry = (int)Math.Round(document.PreviewEntryBeat),
                previewLoopStart = (int)Math.Round(document.PreviewLoopStartBeat),
                previewLoopEnd = (int)Math.Round(document.PreviewLoopEndBeat),
                markers = BuildMarkers(document),
                signatures = [.. document.Signatures.Select(s => new UnitySignature { marker = (float)s.StartBeat, beats = s.Numerator })],
                sections = [.. document.Sections
                    .Select(s => new UnitySection
                    {
                        marker = (float)s.StartBeat,
                        sectionType = ParseInt(s.SectionType),
                        comment = s.Comment ?? string.Empty
                    })]
            };

            return structure;
        }

        private static int[] BuildMarkers(TimelineStructureDocument document)
        {
            if (document.Markers.Count == 0)
            {
                double ticks = Math.Max(1, document.TimeBaseMsPerBeat);
                return [(int)Math.Round(-ticks * 48d), 0];
            }

            List<TimelineMarker> ordered = [.. document.Markers.OrderBy(m => m.BeatIndex)];

            int[] markers = new int[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
                markers[i] = (int)Math.Round(ordered[i].TimeMs * 48d);
            return markers;
        }

        private static int ParseInt(string? value)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
    }
}
