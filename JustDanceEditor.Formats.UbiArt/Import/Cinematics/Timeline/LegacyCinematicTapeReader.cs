using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class LegacyCinematicTapeReader
{
    private static readonly HashSet<uint> PropertyClipTypeIds =
    [
        LegacyBinarySerializer.GetTypeId<LegacyCinematicActorEnableClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicPositionClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicAlphaClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialColorClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicRotationClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicScaleClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicSecondaryTransformClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicProportionClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicDiffuseColorClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicEnableLayerClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicUvRotationClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicUvTranslationClipBinary>(),
        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicUvScaleClipBinary>()
    ];

    public static LegacyCinematicTapeData ReadCinematicTapes(
        JustDanceUbiArtFileSystem fileSystem,
        double videoDurationSeconds,
        ILogger logger)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> tapeDurations = new(StringComparer.OrdinalIgnoreCase);
        List<PropertyClip> propertyClips = [];
        List<SourceEvaluationClip> sourceEvaluationClips = [];
        int parsedTapeCount = 0;
        int order = 0;
        int renderStartFrame = 0;
        int materialTimeStartFrame = 0;

        string mainSequencePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", $"{fileSystem.SongName}_mainsequence.tape");
        VisitTape(LegacyCinematicNames.NormalizePath(mainSequencePath), 0);

        void VisitTape(string tapePath, int timeOffsetFrames)
        {
            if (visited.Count >= LegacyCinematicConstants.MaxTapeVisits)
                return;

            string visitKey = $"{tapePath}@{timeOffsetFrames}";
            if (!visited.Add(visitKey))
                return;

            if (!TryGetLocalTapeClips(fileSystem, tapeClipCache, tapePath, logger, out IReadOnlyList<TapeClip> localClips))
                return;

            parsedTapeCount++;

            IEnumerable<TapeClip> sourceSortedClips = localClips
                .Select((clip, index) => (clip, index))
                .OrderBy(entry => entry.clip.StartFrame + Math.Max(entry.clip.DurationFrames, 0))
                .ThenBy(entry => entry.index)
                .Select(entry => entry.clip);

            foreach (TapeClip localClip in sourceSortedClips)
            {
                TapeClip clip = localClip with { StartFrame = timeOffsetFrames + localClip.StartFrame };
                materialTimeStartFrame = Math.Min(materialTimeStartFrame, clip.StartFrame);

                if (LegacyBinarySerializer.IsTypeId<LegacyCinematicTapeReferenceClipBinary>(clip.TypeId) && clip.Path != null)
                {
                    foreach (TapeVisit childVisit in GetTapeReferenceVisits(
                        tapeDurations,
                        fileSystem,
                        logger,
                        clip,
                        videoDurationSeconds))
                    {
                        VisitTape(childVisit.Path, childVisit.TimeOffsetFrames);
                    }

                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames));
                    continue;
                }

                if ((!PropertyClipTypeIds.Contains(clip.TypeId) && !LegacyBinarySerializer.IsTypeId<LegacyCinematicFxClipBinary>(clip.TypeId)) ||
                    clip.Targets.Count == 0)
                {
                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames));
                    continue;
                }

                CinematicVisualState state = BuildStateFromClip(clip.TypeId, clip.Curves, clip.LayerEnable, clip.MaterialGraphic);
                foreach (ActorTargetPath target in clip.Targets)
                {
                    int propertyOrder = order++;
                    propertyClips.Add(new PropertyClip(
                        target,
                        clip.TypeId,
                        clip.StartFrame,
                        clip.DurationFrames,
                        state,
                        propertyOrder,
                        clip.FxNameId,
                        clip.KillParticlesOnEnd));
                    sourceEvaluationClips.Add(new SourceEvaluationClip(propertyOrder, clip.TypeId, clip.StartFrame, clip.DurationFrames));
                }
            }
        }

        renderStartFrame = GetRenderStartFrameOverride(renderStartFrame, logger);

        logger.LogDebug(
            "Parsed {TapeCount} legacy cinematic tape(s) and {ClipCount} property clip(s); render start frame {RenderStartFrame}; material time start frame {MaterialTimeStartFrame}.",
            parsedTapeCount,
            propertyClips.Count,
            renderStartFrame,
            materialTimeStartFrame);

        return new LegacyCinematicTapeData([.. propertyClips], [.. sourceEvaluationClips], parsedTapeCount, renderStartFrame, materialTimeStartFrame);
    }

    private static bool TryGetLocalTapeClips(
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache,
        string tapePath,
        ILogger logger,
        out IReadOnlyList<TapeClip> clips)
    {
        if (tapeClipCache.TryGetValue(tapePath, out clips!))
            return clips.Count > 0;

        if (!fileSystem.GetFilePath(tapePath, out CookedFile? tapeFile))
        {
            clips = [];
            tapeClipCache[tapePath] = clips;
            return false;
        }

        byte[] bytes = ReadFileBytes(fileSystem, tapeFile);
        clips = [.. ReadTapeClipsSafely(bytes, 0, tapePath, logger)];
        tapeClipCache[tapePath] = clips;
        return true;
    }

    private static IEnumerable<TapeVisit> GetTapeReferenceVisits(
        Dictionary<string, int> tapeDurations,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger,
        TapeClip clip,
        double videoDurationSeconds)
    {
        if (clip.Path == null)
            yield break;

        if (clip.LoopingType is LegacyCinematicTapeReferenceLoopingType.Off or
            LegacyCinematicTapeReferenceLoopingType.Reverse)
        {
            yield return new TapeVisit(clip.Path, clip.StartFrame);
            yield break;
        }

        int tapeDuration = GetTapeDurationFrames(fileSystem, tapeDurations, clip.Path, logger);
        if (tapeDuration <= 0)
        {
            yield return new TapeVisit(clip.Path, clip.StartFrame);
            yield break;
        }

        int referenceDuration = clip.DurationFrames > 0
            ? clip.DurationFrames
            : Math.Max(1, (int)Math.Ceiling(videoDurationSeconds * LegacyCinematicConstants.TapeTicksPerSecond));
        int iterationCount = Math.Max(1, (int)Math.Ceiling(referenceDuration / (double)tapeDuration));
        int maxTimelineFrame = Math.Max(
            clip.StartFrame + referenceDuration,
            (int)Math.Ceiling(videoDurationSeconds * LegacyCinematicConstants.TapeTicksPerSecond));
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            int offset = clip.StartFrame + (iteration * tapeDuration);
            if (offset > maxTimelineFrame)
                break;

            yield return new TapeVisit(clip.Path, offset);
        }
    }

    private static int GetTapeDurationFrames(
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, int> tapeDurations,
        string tapePath,
        ILogger logger)
    {
        if (tapeDurations.TryGetValue(tapePath, out int cachedDuration))
            return cachedDuration;

        if (!fileSystem.GetFilePath(tapePath, out CookedFile? tapeFile))
        {
            tapeDurations[tapePath] = 0;
            return 0;
        }

        byte[] bytes = ReadFileBytes(fileSystem, tapeFile);
        int duration = 0;
        foreach (TapeClip childClip in ReadTapeClipsSafely(bytes, 0, tapePath, logger))
            duration = Math.Max(duration, childClip.StartFrame + Math.Max(0, childClip.DurationFrames));

        tapeDurations[tapePath] = duration;
        return duration;
    }

    public static PropertyClipIndex BuildPropertyClipIndex(
        IReadOnlyList<PropertyClip> clips,
        LegacyCinematicScene scene,
        ILogger logger,
        IReadOnlyList<SourceEvaluationClip>? sourceEvaluationClips = null)
    {
        Dictionary<string, List<PropertyClip>> byActorKey = new(StringComparer.OrdinalIgnoreCase);
        ClipTargetResolver targetResolver = ClipTargetResolver.Create(scene);
        bool debugClipResolution = false;
        string? clipLogPath = null;
        StringBuilder? clipLog = string.IsNullOrWhiteSpace(clipLogPath) ? null : new StringBuilder();
        clipLog?.AppendLine("clip_order,type,start_frame,duration_frames,target_key,resolved_count,resolved_keys,fx_name_id,first_value,first_curve_keys,curve_channels");
        PropertyClip[] tapeSortedClips = [.. clips
            .OrderBy(clip => clip.Order)];
        List<PropertyClip> resolvedSourceClips = new(tapeSortedClips.Length);

        foreach (PropertyClip clip in tapeSortedClips)
        {
            string[] actorKeys = [.. targetResolver.ResolveActorKeys(
                clip.Target,
                includeSubSceneDescendants: ShouldApplyToSubSceneDescendants(clip.TypeId))];
            PropertyClip resolvedClip = clip with { ResolvedActorCount = actorKeys.Length };
            resolvedSourceClips.Add(resolvedClip);
            if (debugClipResolution)
            {
                logger.LogInformation(
                    "Legacy clip {ClipType} start={StartFrame} duration={DurationFrames} target={TargetKey} resolved={ResolvedCount}: {ResolvedKeys}",
                    GetClipTypeName(clip.TypeId),
                    clip.StartFrame,
                    clip.DurationFrames,
                    clip.Target.Key,
                    actorKeys.Length,
                    string.Join(", ", actorKeys.Take(8)));
            }

            AppendClipLogLine(clipLog, resolvedClip, actorKeys);

            foreach (string actorKey in actorKeys)
            {
                if (!byActorKey.TryGetValue(actorKey, out List<PropertyClip>? list))
                {
                    list = [];
                    byActorKey[actorKey] = list;
                }

                bool isResolvedFromAncestor = !string.Equals(actorKey, clip.Target.Key, StringComparison.OrdinalIgnoreCase);
                list.Add(isResolvedFromAncestor
                    ? resolvedClip with { IsResolvedFromAncestor = true }
                    : resolvedClip);
            }
        }

        Dictionary<int, int> resolvedActorCountsByOrder = resolvedSourceClips
            .ToDictionary(clip => clip.Order, clip => clip.ResolvedActorCount);
        IReadOnlyList<SourceEvaluationClip> sourceClipPlayerOrder = sourceEvaluationClips != null
            ? [.. sourceEvaluationClips.Select(clip =>
                clip.PropertyOrder is { } propertyOrder &&
                    resolvedActorCountsByOrder.TryGetValue(propertyOrder, out int resolvedActorCount)
                    ? clip with { ResolvedActorCount = resolvedActorCount }
                    : clip)]
            : [.. resolvedSourceClips
                .OrderBy(clip => clip.StartFrame + Math.Max(clip.DurationFrames, 0))
                .Select(clip => new SourceEvaluationClip(clip.Order, clip.TypeId, clip.StartFrame, clip.DurationFrames, clip.ResolvedActorCount))];

        FlushClipLog(clipLogPath, clipLog);

        return byActorKey.Count == 0
            ? PropertyClipIndex.Empty
            : new PropertyClipIndex(byActorKey, sourceClipPlayerOrder);
    }

    private static void AppendClipLogLine(StringBuilder? builder, PropertyClip clip, IReadOnlyList<string> actorKeys)
    {
        if (builder == null)
            return;

        AppendCsvLine(
            builder,
            clip.Order,
            GetClipTypeName(clip.TypeId),
            clip.StartFrame,
            clip.DurationFrames,
            clip.Target.Key,
            actorKeys.Count,
            string.Join(";", actorKeys),
            clip.FxNameId == 0 ? string.Empty : $"0x{clip.FxNameId:X8}",
            GetFirstStateValue(clip.State),
            GetFirstStateCurveSummary(clip.State),
            GetStateCurveChannelSummary(clip.State));
    }

    private static bool ShouldApplyToSubSceneDescendants(uint clipTypeId) =>
        LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(clipTypeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialColorClipBinary>(clipTypeId);

    private static void FlushClipLog(string? clipLogPath, StringBuilder? builder)
    {
        if (string.IsNullOrWhiteSpace(clipLogPath) || builder == null)
            return;

        string? directory = Path.GetDirectoryName(clipLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(clipLogPath, builder.ToString());
    }

    private static string GetFirstStateValue(CinematicVisualState state)
    {
        if (state.LayerEnable != null)
            return $"{state.LayerEnable.LayerIndex}:{state.LayerEnable.Enabled}";

        foreach (CinematicCurve? curve in EnumerateStateCurves(state))
        {
            if (curve is { Keyframes.Count: > 0 })
                return curve.Keyframes[0].Value.ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string GetFirstStateCurveSummary(CinematicVisualState state)
    {
        foreach ((_, CinematicCurve? curve) in EnumerateStateCurveChannels(state))
        {
            if (curve is { Keyframes.Count: > 0 })
            {
                return string.Join(
                    ";",
                    curve.Keyframes.Select(key =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{key.Time:0.###}:{key.Value:0.######}")));
            }
        }

        return string.Empty;
    }

    private static string GetStateCurveChannelSummary(CinematicVisualState state)
    {
        List<string>? parts = null;
        foreach ((string name, CinematicCurve? curve) in EnumerateStateCurveChannels(state))
        {
            if (curve is not { Keyframes.Count: > 0 })
                continue;

            parts ??= [];
            string summary = string.Join(
                "|",
                curve.Keyframes.Select(key =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{key.Time:0.###}:{key.Value:0.######}")));
            parts.Add($"{name}={summary}");
        }

        return parts == null ? string.Empty : string.Join(";", parts);
    }

    private static IEnumerable<CinematicCurve?> EnumerateStateCurves(CinematicVisualState state)
    {
        foreach ((_, CinematicCurve? curve) in EnumerateStateCurveChannels(state))
            yield return curve;
    }

    private static IEnumerable<(string Name, CinematicCurve? Curve)> EnumerateStateCurveChannels(CinematicVisualState state)
    {
        CinematicTransform? transform = state.Transform;
        if (transform != null)
        {
            yield return ("position_x", transform.PositionX);
            yield return ("position_y", transform.PositionY);
            yield return ("position_z", transform.PositionZ);
            yield return ("rotation_z", transform.Rotation);
            yield return ("scale_x", transform.ScaleX);
            yield return ("scale_y", transform.ScaleY);
            yield return ("rotation_x", transform.RotationX);
            yield return ("rotation_y", transform.RotationY);
        }

        CinematicMaterial? material = state.Material;
        if (material != null)
        {
            yield return ("material_red", material.Red);
            yield return ("material_green", material.Green);
            yield return ("material_blue", material.Blue);
            yield return ("material_alpha", material.Alpha);
        }

        CinematicMaterialGraphicClip? materialGraphic = state.MaterialGraphic;
        if (materialGraphic != null)
        {
            yield return ("material_graphic_red", materialGraphic.Red);
            yield return ("material_graphic_green", materialGraphic.Green);
            yield return ("material_graphic_blue", materialGraphic.Blue);
            yield return ("material_graphic_alpha", materialGraphic.Alpha);
            yield return ("uv_u", materialGraphic.U);
            yield return ("uv_v", materialGraphic.V);
            yield return ("uv_angle", materialGraphic.Angle);
            yield return ("uv_pivot_x", materialGraphic.PivotX);
            yield return ("uv_pivot_y", materialGraphic.PivotY);
            yield return ("uv_scale_u", materialGraphic.ScaleU);
            yield return ("uv_scale_v", materialGraphic.ScaleV);
        }
    }

    private static void AppendCsvLine(StringBuilder builder, params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(',');

            AppendCsvValue(builder, values[i]);
        }

        builder.AppendLine();
    }

    private static void AppendCsvValue(StringBuilder builder, object value)
    {
        string text = value switch
        {
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };

        if (text.Contains('"', StringComparison.Ordinal) ||
            text.Contains(',', StringComparison.Ordinal) ||
            text.Contains('\n', StringComparison.Ordinal) ||
            text.Contains('\r', StringComparison.Ordinal))
        {
            builder.Append('"');
            builder.Append(text.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
            return;
        }

        builder.Append(text);
    }

    private static int GetRenderStartFrameOverride(int renderStartFrame, ILogger logger) => renderStartFrame;

    private static IEnumerable<TapeClip> ReadTapeClipsSafely(
        byte[] bytes,
        int timeOffsetFrames,
        string tapePath,
        ILogger logger)
    {
        using IEnumerator<TapeClip> enumerator = ReadTapeClips(bytes, timeOffsetFrames, logger).GetEnumerator();
        while (true)
        {
            TapeClip clip;
            try
            {
                if (!enumerator.MoveNext())
                    yield break;

                clip = enumerator.Current;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
            {
                logger.LogDebug(ex, "Stopping legacy cinematic tape parse for {TapePath} at malformed clip data.", tapePath);
                yield break;
            }

            yield return clip;
        }
    }

    private static IEnumerable<TapeClip> ReadTapeClips(byte[] bytes, int timeOffsetFrames, ILogger logger)
    {
        LegacyCinematicBinaryReader reader = new(bytes);
        LegacyCinematicTapeHeaderBinary tapeHeader = LegacyBinarySerializer.Deserialize<LegacyCinematicTapeHeaderBinary>(reader);

        if (tapeHeader.Version != 1)
            yield break;

        for (int clipIndex = 0; clipIndex < tapeHeader.ClipCount && reader.Remaining >= 28; clipIndex++)
        {
            if (!SnapToNextTapeClipBoundary(reader, 64))
                yield break;

            int clipOffset = reader.Offset;
            LegacyCinematicTapeClipBinary clipHeader = LegacyBinarySerializer.DeserializeTyped<LegacyCinematicTapeClipBinary>(reader);
            uint typeId = LegacyBinarySerializer.GetTypeId(clipHeader.GetType());
            int serializedSize = clipHeader.SerializedSize;
            int startTime = clipHeader.StartFrame;
            int duration = clipHeader.DurationFrames;
            int absoluteStart = timeOffsetFrames + startTime;

            if (clipHeader is LegacyCinematicTapeReferenceClipBinary)
            {
                string path = LegacyCinematicNames.NormalizePath(reader.ReadPath());
                LegacyCinematicTapeReferenceLoopingType loopingType = ReadTapeReferenceLoopingType(reader);
                yield return new TapeClip(
                    typeId,
                    serializedSize,
                    absoluteStart,
                    duration,
                    path,
                    [],
                    [],
                    LoopingType: loopingType);
                continue;
            }

            if (clipHeader is LegacyCinematicSoundSetClipBinary)
            {
                reader.Skip(4);
                _ = reader.ReadPath();
                yield return new TapeClip(typeId, serializedSize, absoluteStart, duration, null, [], []);
                continue;
            }

            if (clipHeader is LegacyCinematicFxClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                uint fxNameId = reader.ReadUInt32();
                bool killParticlesOnEnd = reader.Remaining >= 4 && reader.ReadUInt32() != 0;
                yield return new TapeClip(
                    typeId,
                    serializedSize,
                    absoluteStart,
                    duration,
                    null,
                    targets,
                    [],
                    FxNameId: fxNameId,
                    KillParticlesOnEnd: killParticlesOnEnd);
                continue;
            }

            if (clipHeader is LegacyCinematicAnimationClipBinary)
            {
                _ = reader.ReadTargetList();
                yield return new TapeClip(typeId, serializedSize, absoluteStart, duration, null, [], []);
                continue;
            }

            if (PropertyClipTypeIds.Contains(typeId))
            {
                if (LegacyBinarySerializer.IsTypeId<LegacyCinematicActorEnableClipBinary>(typeId))
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    int activationValue = reader.ReadInt32();
                    yield return new TapeClip(typeId, serializedSize, absoluteStart, duration, null, targets, [CreateConstantCurve(activationValue != 0 ? 1 : 0)]);
                }
                else if (IsMaterialGraphicClipType(typeId))
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    int layerIndex = reader.ReadInt32();
                    int uvModifierIndex = reader.ReadInt32();
                    CinematicMaterialGraphicClip materialGraphic;
                    CinematicLayerEnable? layerEnable = null;
                    IReadOnlyList<CinematicCurve?> curves = [];
                    if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicEnableLayerClipBinary>(typeId))
                    {
                        bool enabled = reader.ReadInt32() != 0;
                        layerEnable = new CinematicLayerEnable(layerIndex, enabled);
                        materialGraphic = new CinematicMaterialGraphicClip(
                            CinematicMaterialGraphicClipKind.EnableLayer,
                            layerIndex,
                            uvModifierIndex,
                            enabled);
                    }
                    else
                    {
                        curves = reader.ReadCurveBlocks(GetCurveCountForMaterialGraphicClip(typeId));
                        materialGraphic = BuildMaterialGraphicClip(typeId, layerIndex, uvModifierIndex, curves);
                    }

                    yield return new TapeClip(
                        typeId,
                        serializedSize,
                        absoluteStart,
                        duration,
                        null,
                        targets,
                        curves,
                        layerEnable,
                        materialGraphic);
                }
                else
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    int expectedCurveCount = GetCurveCountForPropertyClip(typeId);
                    IReadOnlyList<CinematicCurve?> curves = reader.ReadCurveBlocks(expectedCurveCount);
                    yield return new TapeClip(typeId, serializedSize, absoluteStart, duration, null, targets, curves);
                }

                continue;
            }

            logger.LogDebug(
                "Skipping unsupported legacy cinematic tape clip type 0x{TypeId:X8} at 0x{Offset:X}.",
                typeId,
                clipOffset);
            yield break;
        }
    }

    private static bool SnapToNextTapeClipBoundary(LegacyCinematicBinaryReader reader, int forwardSearchLength)
    {
        if (IsKnownTapeClipTypeAt(reader.Bytes, reader.Offset))
            return true;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 4, reader.Offset + forwardSearchLength);
        for (int offset = reader.Offset + 1; offset <= searchEndOffset; offset++)
        {
            if (!IsKnownTapeClipTypeAt(reader.Bytes, offset))
                continue;

            reader.Offset = offset;
            return true;
        }

        return false;
    }

    private static bool IsKnownTapeClipTypeAt(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return false;

        uint typeId = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return LegacyBinaryTypeRegistry.TryResolve(typeof(LegacyCinematicTapeClipBinary), typeId, out _);
    }

    private static LegacyCinematicTapeReferenceLoopingType ReadTapeReferenceLoopingType(LegacyCinematicBinaryReader reader)
    {
        if (reader.Remaining < 12)
            return LegacyCinematicTapeReferenceLoopingType.Off;

        // TapeReferenceClip serializes an optional resolver dictionary after Path, then the Loop enum.
        // PrinceAli JD2014 references use an empty dictionary, stored as two zero dwords, followed by the enum.
        reader.Skip(8);
        int rawLoop = reader.ReadInt32();
        return Enum.IsDefined(typeof(LegacyCinematicTapeReferenceLoopingType), rawLoop)
            ? (LegacyCinematicTapeReferenceLoopingType)rawLoop
            : LegacyCinematicTapeReferenceLoopingType.Off;
    }

    private static int GetCurveCountForPropertyClip(uint typeId)
    {
        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(typeId))
            return 1;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicRotationClipBinary>(typeId))
            return 3;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialColorClipBinary>(typeId))
            return 3;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicPositionClipBinary>(typeId))
            return 3;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicSecondaryTransformClipBinary>(typeId))
            return 2;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicScaleClipBinary>(typeId))
            return 2;

        return LegacyBinarySerializer.IsTypeId<LegacyCinematicProportionClipBinary>(typeId) ? 2 : 0;
    }

    private static int GetCurveCountForMaterialGraphicClip(uint typeId)
    {
        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary>(typeId))
            return 1;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseColorClipBinary>(typeId))
            return 3;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvTranslationClipBinary>(typeId))
            return 2;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvRotationClipBinary>(typeId))
            return 3;

        return LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvScaleClipBinary>(typeId) ? 4 : 0;
    }

    private static CinematicVisualState BuildStateFromClip(
        uint typeId,
        IReadOnlyList<CinematicCurve?> curves,
        CinematicLayerEnable? layerEnable,
        CinematicMaterialGraphicClip? materialGraphic)
    {
        CinematicTransform? transform = null;
        CinematicMaterial? material = null;

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicActorEnableClipBinary>(typeId))
        {
            material = new CinematicMaterial(null, null, null, curves.ElementAtOrDefault(0));
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialColorClipBinary>(typeId))
        {
            material = new CinematicMaterial(
                curves.ElementAtOrDefault(0),
                curves.ElementAtOrDefault(1),
                curves.ElementAtOrDefault(2),
                curves.ElementAtOrDefault(3));
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(typeId))
        {
            material = new CinematicMaterial(null, null, null, curves.ElementAtOrDefault(0));
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicScaleClipBinary>(typeId) ||
            LegacyBinarySerializer.IsTypeId<LegacyCinematicSecondaryTransformClipBinary>(typeId))
        {
            transform = new CinematicTransform(null, null, null, null, curves.ElementAtOrDefault(0), curves.ElementAtOrDefault(1) ?? curves.ElementAtOrDefault(0));
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicProportionClipBinary>(typeId))
        {
            transform = new CinematicTransform(
                null,
                null,
                null,
                null,
                curves.ElementAtOrDefault(0),
                curves.ElementAtOrDefault(1) ?? curves.ElementAtOrDefault(0),
                CinematicScaleMode.Proportional);
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicRotationClipBinary>(typeId))
        {
            // RotationClip has a deprecated "Curve" field that also targets m_curve_z,
            // but PrinceAli's cooked JD2014 clips use the newer X/Y/Z curve layout.
            // Prefer slot 2 as Z and keep slot 0 as a fallback for genuinely old clips.
            transform = new CinematicTransform(
                null,
                null,
                null,
                curves.ElementAtOrDefault(2) ?? curves.ElementAtOrDefault(0),
                null,
                null,
                RotationX: curves.ElementAtOrDefault(0),
                RotationY: curves.ElementAtOrDefault(1));
        }
        else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicPositionClipBinary>(typeId))
        {
            transform = new CinematicTransform(curves.ElementAtOrDefault(0), curves.ElementAtOrDefault(1), curves.ElementAtOrDefault(2), null, null, null);
        }

        return new CinematicVisualState(transform, material, layerEnable, materialGraphic);
    }

    private static CinematicMaterialGraphicClip BuildMaterialGraphicClip(
        uint typeId,
        int layerIndex,
        int uvModifierIndex,
        IReadOnlyList<CinematicCurve?> curves)
    {
        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary>(typeId))
        {
            return new CinematicMaterialGraphicClip(
                CinematicMaterialGraphicClipKind.DiffuseAlpha,
                layerIndex,
                uvModifierIndex,
                Alpha: curves.ElementAtOrDefault(0));
        }

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseColorClipBinary>(typeId))
        {
            return new CinematicMaterialGraphicClip(
                CinematicMaterialGraphicClipKind.DiffuseColor,
                layerIndex,
                uvModifierIndex,
                Red: curves.ElementAtOrDefault(0),
                Green: curves.ElementAtOrDefault(1),
                Blue: curves.ElementAtOrDefault(2));
        }

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvTranslationClipBinary>(typeId))
        {
            return new CinematicMaterialGraphicClip(
                CinematicMaterialGraphicClipKind.UvTranslation,
                layerIndex,
                uvModifierIndex,
                U: curves.ElementAtOrDefault(0),
                V: curves.ElementAtOrDefault(1));
        }

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvRotationClipBinary>(typeId))
        {
            return new CinematicMaterialGraphicClip(
                CinematicMaterialGraphicClipKind.UvRotation,
                layerIndex,
                uvModifierIndex,
                Angle: curves.ElementAtOrDefault(0),
                PivotX: curves.ElementAtOrDefault(1),
                PivotY: curves.ElementAtOrDefault(2));
        }

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvScaleClipBinary>(typeId))
        {
            return new CinematicMaterialGraphicClip(
                CinematicMaterialGraphicClipKind.UvScale,
                layerIndex,
                uvModifierIndex,
                ScaleU: curves.ElementAtOrDefault(0),
                ScaleV: curves.ElementAtOrDefault(1),
                PivotX: curves.ElementAtOrDefault(2),
                PivotY: curves.ElementAtOrDefault(3));
        }

        throw new InvalidDataException($"Unsupported material graphic clip type 0x{typeId:X8}.");
    }

    private static bool IsMaterialGraphicClipType(uint typeId) =>
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary>(typeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseColorClipBinary>(typeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicEnableLayerClipBinary>(typeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvRotationClipBinary>(typeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvTranslationClipBinary>(typeId) ||
        LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvScaleClipBinary>(typeId);

    private static string GetClipTypeName(uint typeId)
    {
        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicActorEnableClipBinary>(typeId))
            return "ActorEnable";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicPositionClipBinary>(typeId))
            return "Position";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(typeId))
            return "Alpha";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialColorClipBinary>(typeId))
            return "MaterialColor";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicRotationClipBinary>(typeId))
            return "Rotation";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicScaleClipBinary>(typeId))
            return "Scale";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicSecondaryTransformClipBinary>(typeId))
            return "SecondaryTransform";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicProportionClipBinary>(typeId))
            return "Proportion";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary>(typeId))
            return "MaterialGraphicDiffuseAlpha";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicDiffuseColorClipBinary>(typeId))
            return "MaterialGraphicDiffuseColor";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicEnableLayerClipBinary>(typeId))
            return "MaterialGraphicEnableLayer";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvRotationClipBinary>(typeId))
            return "MaterialGraphicUVRotation";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvTranslationClipBinary>(typeId))
            return "MaterialGraphicUVTranslation";

        if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicUvScaleClipBinary>(typeId))
            return "MaterialGraphicUVScale";

        return LegacyBinarySerializer.IsTypeId<LegacyCinematicFxClipBinary>(typeId)
            ? "FxClip"
            : $"0x{typeId:X8}";
    }

    private static CinematicCurve CreateConstantCurve(float value) =>
        new([new CinematicKeyframe(0, value, 0, value, 0, value)]);

    private static byte[] ReadFileBytes(JustDanceUbiArtFileSystem fileSystem, CookedFile file)
    {
        using Stream stream = fileSystem.GetFileStream(file);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}