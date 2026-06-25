using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicTapeClipParser
{
    internal static IEnumerable<TapeClip> ReadTapeClipsSafely(
        byte[] bytes,
        int timeOffsetFrames,
        string tapePath,
        ILogger logger,
        LegacyBinarySerializerContext serializerContext)
    {
        using IEnumerator<TapeClip> enumerator = ReadTapeClips(bytes, timeOffsetFrames, logger, serializerContext).GetEnumerator();
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
                logger.LogDebug(ex, "Stopping cinematic tape parse for {TapePath} at malformed clip data.", tapePath);
                yield break;
            }

            yield return clip;
        }
    }

    internal static string ReadSoundSetPath(
        CinematicBinaryReader reader,
        LegacyBinarySerializerContext serializerContext)
    {
        if (serializerContext.EngineVersion is null or < 2015)
            reader.Skip(4);

        return CinematicNames.NormalizePath(reader.ReadPath());
    }

    private static IEnumerable<TapeClip> ReadTapeClips(
        byte[] bytes,
        int timeOffsetFrames,
        ILogger logger,
        LegacyBinarySerializerContext serializerContext)
    {
        CinematicBinaryReader reader = new(bytes);
        CinematicTapeHeaderBinary tapeHeader = LegacyBinarySerializer.Deserialize<CinematicTapeHeaderBinary>(reader);

        if (tapeHeader.Version != 1)
            yield break;

        for (int clipIndex = 0; clipIndex < tapeHeader.ClipCount && reader.Remaining >= 28; clipIndex++)
        {
            if (!SnapToNextTapeClipBoundary(reader, 64))
                yield break;

            int clipOffset = reader.Offset;
            CinematicTapeClipBinary clipHeader = LegacyBinarySerializer.DeserializeTyped<CinematicTapeClipBinary>(reader);
            uint typeId = LegacyBinarySerializer.GetTypeId(clipHeader.GetType());
            int startTime = clipHeader.StartFrame;
            int duration = clipHeader.DurationFrames;
            int absoluteStart = timeOffsetFrames + startTime;

            if (clipHeader is CinematicTapeReferenceClipBinary)
            {
                string path = CinematicNames.NormalizePath(reader.ReadPath());
                CinematicTapeReferenceLoopingType loopingType = ReadTapeReferenceLoopingType(reader);
                yield return new TapeClip(
                    typeId,
                    absoluteStart,
                    duration,
                    path,
                    [],
                    [],
                    LoopingType: loopingType);
                continue;
            }

            if (clipHeader is CinematicSoundSetClipBinary)
            {
                string soundSetPath = ReadSoundSetPath(reader, serializerContext);
                yield return new TapeClip(typeId, absoluteStart, duration, soundSetPath, [], []);
                continue;
            }

            if (clipHeader is CinematicFxClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                uint fxNameId = reader.ReadUInt32();
                bool killParticlesOnEnd = reader.Remaining >= 4 && reader.ReadUInt32() != 0;
                yield return new TapeClip(
                    typeId,
                    absoluteStart,
                    duration,
                    null,
                    targets,
                    [],
                    FxNameId: fxNameId,
                    KillParticlesOnEnd: killParticlesOnEnd);
                continue;
            }

            if (clipHeader is CinematicAnimationClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                uint animationNameId = reader.ReadUInt32();
                bool loop = reader.ReadInt32() != 0;
                float playRate = reader.ReadSingle();
                int animationStartFrame = reader.ReadInt32();
                yield return new TapeClip(
                    typeId,
                    absoluteStart,
                    duration,
                    null,
                    targets,
                    [],
                    Animation: new CinematicAnimationClip(
                        animationNameId == uint.MaxValue
                            ? string.Empty
                            : $"0x{animationNameId:X8}",
                        loop,
                        playRate,
                        animationStartFrame));
                continue;
            }

            if (clipHeader is CinematicSpawnActorClipBinary)
            {
                string actorPath = CinematicNames.NormalizePath(reader.ReadPath());
                string actorName = reader.ReadString();
                float positionX = reader.ReadSingle();
                float positionY = reader.ReadSingle();
                float positionZ = reader.ReadSingle();
                ActorTargetPath? parentTarget = ReadOptionalObjectPath(reader);
                yield return new TapeClip(
                    typeId,
                    absoluteStart,
                    duration,
                    null,
                    [],
                    [],
                    SpawnActor: new CinematicSpawnActorClip(
                        actorPath,
                        actorName,
                        positionX,
                        positionY,
                        positionZ,
                        parentTarget));
                continue;
            }

            if (clipHeader is CinematicTapeLauncherClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                CinematicTapeLauncherClip? launcher = ReadTapeLauncherClip(reader);
                yield return new TapeClip(typeId, absoluteStart, duration, null, targets, [], TapeLauncher: launcher);
                continue;
            }

            if (clipHeader is CinematicGameplayEventClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                if (reader.Remaining >= 4)
                    reader.SkipInt32();

                if (reader.Remaining >= 4)
                    reader.SkipString();

                yield return new TapeClip(typeId, absoluteStart, duration, null, targets, []);
                continue;
            }

            if (clipHeader is CinematicTextClipBinary)
            {
                IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                if (reader.Remaining >= 4)
                    reader.SkipUInt32();

                yield return new TapeClip(typeId, absoluteStart, duration, null, targets, []);
                continue;
            }

            if (clipHeader is CinematicMarkerClipBinary)
            {
                if (reader.Remaining >= 4)
                    reader.SkipString();

                yield return new TapeClip(typeId, absoluteStart, duration, null, [], []);
                continue;
            }

            if (clipHeader is CinematicMashupClipBinary or CinematicSlotClipBinary or CinematicVideoClipBinary)
            {
                ReadMashupClipPayload(reader, clipHeader);
                yield return new TapeClip(typeId, absoluteStart, duration, null, [], []);
                continue;
            }

            if (CinematicTapeClipTypes.IsPropertyClip(typeId))
            {
                if (LegacyBinarySerializer.IsTypeId<CinematicPivotClipBinary>(typeId))
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    IReadOnlyList<CinematicCurve?> curves = reader.ReadCurveBlocks(2);
                    TextureAnchor? anchor = reader.Remaining >= 4
                        ? ReadTextureAnchorOrNull(reader.ReadInt32())
                        : TextureAnchor.MiddleCenter;
                    CinematicPivotClip pivot = new(
                        anchor,
                        curves.ElementAtOrDefault(0),
                        curves.ElementAtOrDefault(1));
                    yield return new TapeClip(
                        typeId,
                        absoluteStart,
                        duration,
                        null,
                        targets,
                        curves,
                        Pivot: pivot);
                }
                else if (LegacyBinarySerializer.IsTypeId<CinematicActorEnableClipBinary>(typeId))
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    int activationValue = reader.ReadInt32();
                    yield return new TapeClip(typeId, absoluteStart, duration, null, targets, [CinematicVisualStateBuilder.CreateConstantCurve(activationValue != 0 ? 1 : 0)]);
                }
                else if (CinematicTapeClipTypes.IsMaterialGraphicClipType(typeId))
                {
                    IReadOnlyList<ActorTargetPath> targets = reader.ReadTargetList();
                    int layerIndex = reader.ReadInt32();
                    int uvModifierIndex = reader.ReadInt32();
                    CinematicMaterialGraphicClip materialGraphic;
                    CinematicLayerEnable? layerEnable = null;
                    IReadOnlyList<CinematicCurve?> curves = [];
                    if (LegacyBinarySerializer.IsTypeId<CinematicMaterialGraphicEnableLayerClipBinary>(typeId))
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
                        curves = reader.ReadCurveBlocks(CinematicTapeClipTypes.GetCurveCountForMaterialGraphicClip(typeId));
                        materialGraphic = CinematicVisualStateBuilder.BuildMaterialGraphicClip(typeId, layerIndex, uvModifierIndex, curves);
                    }

                    yield return new TapeClip(
                        typeId,
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
                    int expectedCurveCount = CinematicTapeClipTypes.GetCurveCountForPropertyClip(typeId);
                    IReadOnlyList<CinematicCurve?> curves = reader.ReadCurveBlocks(expectedCurveCount);
                    yield return new TapeClip(typeId, absoluteStart, duration, null, targets, curves);
                }

                continue;
            }

            logger.LogDebug(
                "Skipping unsupported cinematic tape clip type 0x{TypeId:X8} at 0x{Offset:X}.",
                typeId,
                clipOffset);
            yield break;
        }
    }

    private static CinematicTapeLauncherClip? ReadTapeLauncherClip(CinematicBinaryReader reader)
    {
        if (reader.Remaining < 8)
            return null;

        int action = reader.ReadInt32();
        int tapeChoice = reader.ReadInt32();
        string? deprecatedTapeLabel = TryReadLegacyTapeLabel(reader);
        IReadOnlyList<string> tapeLabels = TryReadTapeLabelContainer(reader);
        return new CinematicTapeLauncherClip(action, tapeChoice, deprecatedTapeLabel, tapeLabels);
    }

    private static string? TryReadLegacyTapeLabel(CinematicBinaryReader reader)
    {
        if (reader.Remaining < 4)
            return null;

        int offset = reader.Offset;
        try
        {
            string label = reader.ReadString();
            if (!IsPlausibleTapeLabel(label))
            {
                reader.Offset = offset;
                return null;
            }

            return label;
        }
        catch (InvalidDataException)
        {
            reader.Offset = offset;
            return null;
        }
    }

    private static IReadOnlyList<string> TryReadTapeLabelContainer(CinematicBinaryReader reader)
    {
        if (reader.Remaining < 4)
            return [];

        int offset = reader.Offset;
        try
        {
            int count = reader.ReadInt32();
            if (count is < 0 or > 256)
            {
                reader.Offset = offset;
                return [];
            }

            List<string> labels = new(count);
            for (int i = 0; i < count; i++)
            {
                string label = reader.ReadString();
                if (IsPlausibleTapeLabel(label))
                    labels.Add(label);
            }

            return labels;
        }
        catch (InvalidDataException)
        {
            reader.Offset = offset;
            return [];
        }
    }

    private static bool IsPlausibleTapeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 128)
            return false;

        foreach (char c in label)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or '\\')
                continue;

            return false;
        }

        return true;
    }

    private static void ReadMashupClipPayload(
        CinematicBinaryReader reader,
        CinematicTapeClipBinary clipHeader)
    {
        // MashupClip serializes Bpm, Signature, and Guid. SlotClip adds no
        // fields; VideoClip appends offset/scale values used by Pleo modifiers.
        if (reader.Remaining >= 4)
            reader.SkipSingle();

        if (reader.Remaining >= 4)
            reader.SkipString();

        if (reader.Remaining >= 4)
            reader.SkipString();

        if (clipHeader is CinematicVideoClipBinary && reader.Remaining >= 12)
        {
            reader.SkipSingle();
            reader.SkipSingle();
            reader.SkipSingle();
        }
    }

    private static ActorTargetPath? ReadOptionalObjectPath(CinematicBinaryReader reader)
    {
        if (reader.Remaining < 16)
            return null;

        int start = reader.Offset;
        int tableSize = reader.ReadInt32();
        int segmentCount = reader.ReadInt32();
        if (tableSize != 0x30 || segmentCount < 0 || segmentCount > 32)
        {
            reader.Offset = start;
            return null;
        }

        if (segmentCount == 0)
        {
            reader.Skip(Math.Min(reader.Remaining, 8));
            return null;
        }

        reader.Offset = start;
        try
        {
            return reader.ReadTargetDescriptor();
        }
        catch (InvalidDataException)
        {
            reader.Offset = start;
            return null;
        }
    }

    private static TextureAnchor? ReadTextureAnchorOrNull(int value)
    {
        if (!Enum.IsDefined(typeof(TextureAnchor), value))
            return null;

        TextureAnchor anchor = (TextureAnchor)value;
        return anchor == TextureAnchor.None ? null : anchor;
    }

    private static bool SnapToNextTapeClipBoundary(CinematicBinaryReader reader, int forwardSearchLength)
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
        return LegacyBinaryTypeRegistry.TryResolve(typeof(CinematicTapeClipBinary), typeId, out _);
    }

    private static CinematicTapeReferenceLoopingType ReadTapeReferenceLoopingType(CinematicBinaryReader reader)
    {
        if (reader.Remaining < 12)
            return CinematicTapeReferenceLoopingType.Off;

        // TapeReferenceClip serializes an optional resolver dictionary after Path, then the Loop enum.
        // PrinceAli JD2014 references use an empty dictionary, stored as two zero dwords, followed by the enum.
        reader.Skip(8);
        int rawLoop = reader.ReadInt32();
        return Enum.IsDefined(typeof(CinematicTapeReferenceLoopingType), rawLoop)
            ? (CinematicTapeReferenceLoopingType)rawLoop
            : CinematicTapeReferenceLoopingType.Off;
    }
}