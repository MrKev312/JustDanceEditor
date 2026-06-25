using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

using KevInc.UbiArt.Cinematics.Particles;

using System.Buffers.Binary;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class CinematicParticleTemplateReader
{
    private const int AabbSerializedSize = 0x10;
    private const int PhaseRecordMarker = 0x54;
    private const int PhaseRecordMarkerCompact = 0x44;
    private const int CurveRecordMarker = 0x48;
    private const int CurveRecordMarkerCompact = 0x44;
    private const int SplineRecordMarker = 0x24;
    private const int SplineRecordMarkerCompact = 0x20;
    private const int TemplateAnimationTailSize = 20;
    private const int SerializedParamsObjectHeaderSize = 4;
    private const int SerializedParticleCurveCount = 26;
    private const int CurvePositionIndex = 0;
    private const int CurveAngleIndex = 1;
    private const int CurveVelocityMultIndex = 2;
    private const int CurveAccelerationXIndex = 3;
    private const int CurveAccelerationYIndex = 4;
    private const int CurveAccelerationZIndex = 5;
    private const int CurveSizeIndex = 6;
    private const int CurveSizeYIndex = 7;
    private const int CurveAlphaIndex = 8;
    private const int CurveRgbIndex = 9;
    private const int CurveRgb1Index = 10;
    private const int CurveRgb2Index = 11;
    private const int CurveRgb3Index = 12;
    private const int CurveAnimationIndex = 13;
    private const int CurveEmitVelocityIndex = 14;
    private const int CurveEmitVelocityAngleIndex = 15;
    private const int CurveEmitAngleIndex = 16;
    private const int CurveEmitAngularSpeedIndex = 17;
    private const int CurveFrequencyIndex = 18;
    private const int CurveParticleLifeTimeIndex = 19;
    private const int CurveEmitAlphaIndex = 20;
    private const int CurveEmitColorFactorIndex = 21;
    private const int CurveEmitSizeXyIndex = 22;
    private const int CurveEmitAccelerationIndex = 23;
    private const int CurveEmitGravityIndex = 24;
    private const int CurveEmitAnimationIndex = 25;
    private const int CurveFixedSize = 0x30;
    private const int CurvePointSize = 0x48;
    private const uint ParticleGenerationPoints = 0;
    private const uint ParticleGenerationHemisphere = 5;
    private const uint ParticleModeFollow = 0;
    private const uint ParticleModeManual = 2;
    private const uint ParticleEmitModeOverTime = 0;
    private const uint ParticleEmitModeOverDistance = 2;
    private const int MaxReasonableParticleCount = 10000;
    private const int MaxReasonablePhaseCount = 64;
    private const int MaxReasonableAtlasFrame = 4096;
    private const int MaxReasonableCurvePointCount = 1024;

    public static bool TryRead(byte[] bytes, out CinematicParticleTemplate template)
    {
        template = default!;
        for (int offset = 0; offset <= bytes.Length - 4; offset++)
        {
            if (TryReadAt(bytes, offset, allowEmptyPhaseList: false, out template))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<CinematicParticleTemplate> ReadAll(byte[] bytes, bool allowEmptyPhaseList = false)
    {
        List<CinematicParticleTemplate> templates = [];
        HashSet<int> sourceOffsets = [];
        for (int offset = 0; offset <= bytes.Length - 4; offset++)
        {
            if (!TryReadAt(bytes, offset, allowEmptyPhaseList, out CinematicParticleTemplate? template) ||
                !sourceOffsets.Add(template.Parameters.SourceOffset))
            {
                continue;
            }

            templates.Add(template);
        }

        return [.. templates.OrderBy(template => template.Parameters.SourceOffset)];
    }

    private static bool TryReadAt(
        byte[] bytes,
        int offset,
        bool allowEmptyPhaseList,
        out CinematicParticleTemplate template)
    {
        template = default!;
        try
        {
            CinematicBinaryReader reader = new(bytes, offset);
            uint maxParticles = reader.ReadUInt32();
            if (maxParticles is 0 or > MaxReasonableParticleCount)
                return false;

            CinematicParticleColor defaultColor = ReadColor(reader);
            if (!IsNormalized(defaultColor.Blue) ||
                !IsNormalized(defaultColor.Green) ||
                !IsNormalized(defaultColor.Red) ||
                !IsNormalized(defaultColor.Alpha))
            {
                return false;
            }

            uint emitParticlesCount = reader.ReadUInt32();
            bool forceNoDynamicFog = ReadBool(reader, out bool validBool);
            if (!validBool)
                return false;

            bool renderInReflection = ReadBool(reader, out validBool);
            if (!validBool)
                return false;

            CinematicParticleParameters parameters = new(
                offset,
                maxParticles,
                defaultColor,
                emitParticlesCount,
                forceNoDynamicFog,
                renderInReflection,
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadVector3(reader),
                ReadVector2(reader),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadVector3(reader),
                ReadVector3(reader),
                reader.ReadSingle(),
                ReadBool(reader, out _),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadBool(reader, out _),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadVector3(reader),
                ReadVector3(reader),
                ReadBool(reader, out _),
                reader.ReadUInt32(),
                ReadBool(reader, out _),
                ReadBox(reader),
                reader.ReadSingle(),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadBox(reader),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                reader.ReadUInt32(),
                reader.ReadSingle(),
                reader.ReadUInt32(),
                ReadBool(reader, out _),
                ReadBool(reader, out _),
                ReadVector2(reader),
                ReadBool(reader, out _));

            if (!TryFindPhaseList(bytes, reader.Offset, out int phaseListOffset, out uint serializedPhaseCount))
            {
                if (!allowEmptyPhaseList ||
                    !TryFindEmptyPhaseList(bytes, reader.Offset, out phaseListOffset))
                {
                    return false;
                }
            }

            if (serializedPhaseCount != parameters.PhaseCount)
                parameters = parameters with { PhaseCount = serializedPhaseCount };

            reader.Offset = phaseListOffset + 4;
            List<CinematicParticlePhase> phases = new((int)serializedPhaseCount);
            for (int phaseIndex = 0; phaseIndex < serializedPhaseCount; phaseIndex++)
            {
                int phaseMarker = reader.ReadInt32();
                if (phaseMarker != PhaseRecordMarker)
                    return false;

                phases.Add(ReadPhase(reader));
            }

            if (serializedPhaseCount == 0)
            {
                phases.Add(CreateDefaultPhase());
                parameters = parameters with { PhaseCount = 1 };
            }

            CinematicParticleCurves curves = ReadParticleCurves(bytes, reader.Offset, out int afterCurvesOffset);
            CinematicParticleModeTail modeTail = ReadModeTail(bytes, afterCurvesOffset);
            parameters = parameters with
            {
                GenerationType = modeTail.GenerationType,
                GeneratorMode = modeTail.GeneratorMode,
                GeneratorEmitMode = modeTail.GeneratorEmitMode
            };

            template = new CinematicParticleTemplate(
                ReadAnimationTail(bytes, offset),
                parameters,
                phases,
                curves);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryFindPhaseList(byte[] bytes, int searchStartOffset, out int phaseListOffset, out uint phaseCount)
    {
        phaseListOffset = -1;
        phaseCount = 0;

        int searchEndOffset = Math.Min(bytes.Length - 8, searchStartOffset + 0x80);
        for (int offset = searchStartOffset; offset <= searchEndOffset; offset += 4)
        {
            uint candidateCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (candidateCount is 0 or > MaxReasonablePhaseCount)
                continue;

            int marker = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4));
            if (!IsPhaseRecordMarker(marker))
                continue;

            phaseListOffset = offset;
            phaseCount = candidateCount;
            return true;
        }

        return false;
    }

    private static bool TryFindEmptyPhaseList(byte[] bytes, int searchStartOffset, out int phaseListOffset)
    {
        phaseListOffset = -1;
        if (searchStartOffset < 0 || searchStartOffset + 8 > bytes.Length)
            return false;

        uint candidateCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(searchStartOffset, 4));
        if (candidateCount != 0)
            return false;

        int nextObjectSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(searchStartOffset + 4, 4));
        if (nextObjectSize is not (0 or CurveRecordMarker or CurveRecordMarkerCompact))
            return false;

        phaseListOffset = searchStartOffset;
        return true;
    }

    private static CinematicParticlePhase CreateDefaultPhase() =>
        new(
            1.0f,
            new CinematicParticleColor(1, 1, 1, 1),
            new CinematicParticleColor(1, 1, 1, 1),
            new Vector2(1, 1),
            new Vector2(1, 1),
            -1,
            -1,
            uint.MaxValue,
            0.0f,
            false,
            true);

    private static CinematicParticlePhase ReadPhase(CinematicBinaryReader reader) =>
        new(
            reader.ReadSingle(),
            ReadColor(reader),
            ReadColor(reader),
            ReadVector2(reader),
            ReadVector2(reader),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadSingle(),
            ReadBool(reader, out _),
            ReadBool(reader, out _));

    private static CinematicParticleColor ReadColor(CinematicBinaryReader reader)
    {
        float blue = reader.ReadSingle();
        float green = reader.ReadSingle();
        float red = reader.ReadSingle();
        float alpha = reader.ReadSingle();
        return new CinematicParticleColor(red, green, blue, alpha);
    }

    private static Vector2 ReadVector2(CinematicBinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    private static Vector3 ReadVector3(CinematicBinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static CinematicParticleBox ReadBox(CinematicBinaryReader reader)
    {
        int size = reader.ReadInt32();
        if (size != AabbSerializedSize)
            throw new InvalidDataException($"Invalid legacy particle box marker 0x{size:X8} at 0x{reader.Offset - 4:X}.");

        return new CinematicParticleBox(ReadVector2(reader), ReadVector2(reader));
    }

    private static bool ReadBool(CinematicBinaryReader reader, out bool valid)
    {
        uint raw = reader.ReadUInt32();
        valid = raw <= 1;
        return raw != 0;
    }

    private static bool IsNormalized(float value) =>
        value is >= -0.0001f and <= 1.0001f;

    private static CinematicParticleAnimation ReadAnimationTail(byte[] bytes, int parametersOffset)
    {
        // The animation tail sits immediately before the cooked params object.
        // Some files include the params object's four-byte size header before
        // the parameter payload this reader starts at.
        int offset = parametersOffset - TemplateAnimationTailSize - SerializedParamsObjectHeaderSize;
        if (TryReadAnimationTailAt(bytes, offset, out CinematicParticleAnimation animation))
            return animation;

        return TryReadAnimationTailAt(bytes, parametersOffset - TemplateAnimationTailSize, out animation)
            ? animation
            : CinematicParticleAnimation.Default;
    }

    private static bool TryReadAnimationTailAt(
        byte[] bytes,
        int offset,
        out CinematicParticleAnimation animation)
    {
        animation = CinematicParticleAnimation.Default;
        if (offset < 0 || offset + TemplateAnimationTailSize > bytes.Length)
            return false;

        bool loop = TryReadTemplateLoopAt(bytes, offset - 20);
        uint useUvRandomRaw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        int startAnimIndex = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4));
        int endAnimIndex = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8, 4));
        uint animName = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12, 4));
        float animUvFrequency = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 16, 4)));

        bool bothUnset = startAnimIndex == -1 && endAnimIndex == -1;
        bool bothSet = startAnimIndex >= 0 && endAnimIndex >= 0;
        if (useUvRandomRaw > 1 ||
            (!bothUnset && !bothSet) ||
            startAnimIndex > MaxReasonableAtlasFrame ||
            endAnimIndex > MaxReasonableAtlasFrame ||
            !float.IsFinite(animUvFrequency) ||
            Math.Abs(animUvFrequency) > 240.0f)
        {
            return false;
        }

        animation = new CinematicParticleAnimation(
            loop,
            useUvRandomRaw != 0,
            startAnimIndex,
            endAnimIndex,
            animName,
            animUvFrequency == 0 ? 1.0f : animUvFrequency);
        return true;
    }

    private static bool TryReadTemplateLoopAt(byte[] bytes, int offset)
    {
        // The loop flag appears before the animation tail, with the cooked empty
        // animation path occupying the bytes between loop and useUVRandom in the
        // JD2014 templates seen so far.
        if (offset < 0 || offset + 4 > bytes.Length)
            return false;

        uint raw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return raw == 1;
    }

    private static CinematicParticleCurves ReadParticleCurves(byte[] bytes, int offset, out int nextOffset)
    {
        if (!TryReadParticleCurves(bytes, offset, out CinematicParticleCurves curves, out nextOffset))
        {
            nextOffset = offset;
            return CinematicParticleCurves.Empty;
        }

        return curves;
    }

    private static bool TryReadParticleCurves(
        byte[] bytes,
        int offset,
        out CinematicParticleCurves curves,
        out int nextOffset)
    {
        curves = CinematicParticleCurves.Empty;
        nextOffset = offset;
        if (offset < 0 || offset >= bytes.Length)
            return false;

        CinematicParticleCurve position = CinematicParticleCurve.Empty;
        CinematicParticleCurve angle = CinematicParticleCurve.Empty;
        CinematicParticleCurve velocityMult = CinematicParticleCurve.Empty;
        CinematicParticleCurve accelerationX = CinematicParticleCurve.Empty;
        CinematicParticleCurve accelerationY = CinematicParticleCurve.Empty;
        CinematicParticleCurve accelerationZ = CinematicParticleCurve.Empty;
        CinematicParticleCurve atlasAnimation = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitAtlasAnimation = CinematicParticleCurve.Empty;
        CinematicParticleCurve size = CinematicParticleCurve.Empty;
        CinematicParticleCurve sizeY = CinematicParticleCurve.Empty;
        CinematicParticleCurve alpha = CinematicParticleCurve.Empty;
        CinematicParticleCurve rgb = CinematicParticleCurve.Empty;
        CinematicParticleCurve rgb1 = CinematicParticleCurve.Empty;
        CinematicParticleCurve rgb2 = CinematicParticleCurve.Empty;
        CinematicParticleCurve rgb3 = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitVelocity = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitVelocityAngle = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitAngle = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitAngularSpeed = CinematicParticleCurve.Empty;
        CinematicParticleCurve frequency = CinematicParticleCurve.Empty;
        CinematicParticleCurve particleLifeTime = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitAlpha = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitColorFactor = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitSizeXy = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitAcceleration = CinematicParticleCurve.Empty;
        CinematicParticleCurve emitGravity = CinematicParticleCurve.Empty;
        int cursor = offset;
        for (int curveIndex = 0; curveIndex < SerializedParticleCurveCount; curveIndex++)
        {
            if (!TryReadParticleCurve(bytes, cursor, out CinematicParticleCurve curve, out int curveNextOffset))
                return false;

            switch (curveIndex)
            {
                case CurvePositionIndex:
                    position = curve;
                    break;
                case CurveAngleIndex:
                    angle = curve;
                    break;
                case CurveVelocityMultIndex:
                    velocityMult = curve;
                    break;
                case CurveAccelerationXIndex:
                    accelerationX = curve;
                    break;
                case CurveAccelerationYIndex:
                    accelerationY = curve;
                    break;
                case CurveAccelerationZIndex:
                    accelerationZ = curve;
                    break;
                case CurveSizeIndex:
                    size = curve;
                    break;
                case CurveSizeYIndex:
                    sizeY = curve;
                    break;
                case CurveAlphaIndex:
                    alpha = curve;
                    break;
                case CurveRgbIndex:
                    rgb = curve;
                    break;
                case CurveRgb1Index:
                    rgb1 = curve;
                    break;
                case CurveRgb2Index:
                    rgb2 = curve;
                    break;
                case CurveRgb3Index:
                    rgb3 = curve;
                    break;
                case CurveAnimationIndex:
                    atlasAnimation = curve;
                    break;
                case CurveEmitVelocityIndex:
                    emitVelocity = curve;
                    break;
                case CurveEmitVelocityAngleIndex:
                    emitVelocityAngle = curve;
                    break;
                case CurveEmitAngleIndex:
                    emitAngle = curve;
                    break;
                case CurveEmitAngularSpeedIndex:
                    emitAngularSpeed = curve;
                    break;
                case CurveFrequencyIndex:
                    frequency = curve;
                    break;
                case CurveParticleLifeTimeIndex:
                    particleLifeTime = curve;
                    break;
                case CurveEmitAlphaIndex:
                    emitAlpha = curve;
                    break;
                case CurveEmitColorFactorIndex:
                    emitColorFactor = curve;
                    break;
                case CurveEmitSizeXyIndex:
                    emitSizeXy = curve;
                    break;
                case CurveEmitAccelerationIndex:
                    emitAcceleration = curve;
                    break;
                case CurveEmitGravityIndex:
                    emitGravity = curve;
                    break;
                case CurveEmitAnimationIndex:
                    emitAtlasAnimation = curve;
                    break;
            }

            cursor = curveNextOffset;
        }

        nextOffset = cursor;
        curves = new CinematicParticleCurves(
            position,
            angle,
            velocityMult,
            accelerationX,
            accelerationY,
            accelerationZ,
            size,
            sizeY,
            alpha,
            rgb,
            rgb1,
            rgb2,
            rgb3,
            atlasAnimation,
            emitVelocity,
            emitVelocityAngle,
            emitAngle,
            emitAngularSpeed,
            frequency,
            particleLifeTime,
            emitAlpha,
            emitColorFactor,
            emitSizeXy,
            emitAcceleration,
            emitGravity,
            emitAtlasAnimation);
        return true;
    }

    private static CinematicParticleModeTail ReadModeTail(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 12 > bytes.Length)
            return CinematicParticleModeTail.Default;

        uint generationType = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        uint generatorMode = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 4, 4));
        uint emitMode = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
        if (generationType is < ParticleGenerationPoints or > ParticleGenerationHemisphere ||
            generatorMode is < ParticleModeFollow or > ParticleModeManual ||
            emitMode is < ParticleEmitModeOverTime or > ParticleEmitModeOverDistance)
        {
            return CinematicParticleModeTail.Default;
        }

        return new CinematicParticleModeTail(generationType, generatorMode, emitMode);
    }

    private static bool TryReadParticleCurve(
        byte[] bytes,
        int offset,
        out CinematicParticleCurve curve,
        out int nextOffset)
    {
        curve = CinematicParticleCurve.Empty;
        nextOffset = offset;
        if (offset < 0 || offset + CurveFixedSize > bytes.Length)
            return false;

        if (!IsCurveRecordMarker(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))) ||
            !IsSplineRecordMarker(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 32, 4))))
        {
            return false;
        }

        float baseTime = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4)));
        Vector3 outputMin = ReadVector3(bytes, offset + 8);
        Vector3 outputMax = ReadVector3(bytes, offset + 20);
        uint pointCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 36, 4));
        if (pointCount > MaxReasonableCurvePointCount)
            return false;

        int pointsOffset = offset + 40;
        int serializedSize = CurveFixedSize + checked((int)pointCount * CurvePointSize);
        if (offset + serializedSize > bytes.Length)
            return false;

        List<CinematicParticleCurvePoint> points = new((int)pointCount);
        int cursor = pointsOffset;
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            if (!IsCurveRecordMarker(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor, 4))))
                return false;

            Vector3 value = ReadVector3(bytes, cursor + 4);
            float time = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor + 16, 4)));
            Vector3 normalIn = ReadVector3(bytes, cursor + 20);
            Vector3 normalInTime = ReadVector3(bytes, cursor + 32);
            Vector3 normalOut = ReadVector3(bytes, cursor + 44);
            Vector3 normalOutTime = ReadVector3(bytes, cursor + 56);
            int interpolation = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor + 68, 4));
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z) ||
                !float.IsFinite(time) ||
                !IsFinite(normalIn) ||
                !IsFinite(normalInTime) ||
                !IsFinite(normalOut) ||
                !IsFinite(normalOutTime) ||
                interpolation is < 0 or > 4)
            {
                return false;
            }

            points.Add(new CinematicParticleCurvePoint(
                time,
                value,
                normalIn,
                normalInTime,
                normalOut,
                normalOutTime,
                interpolation));
            cursor += CurvePointSize;
        }

        if (!float.IsFinite(baseTime) ||
            !IsFinite(outputMin) ||
            !IsFinite(outputMax))
        {
            return false;
        }

        int splineTailOffset = pointsOffset + checked((int)pointCount * CurvePointSize);
        bool loop = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(splineTailOffset, 4)) != 0;
        float loopTime = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(splineTailOffset + 4, 4)));
        if (!float.IsFinite(loopTime))
            loopTime = 0.0f;

        curve = new CinematicParticleCurve(baseTime, outputMin, outputMax, points, loop, loopTime);
        nextOffset = offset + serializedSize;
        return true;
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset) =>
        new(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8, 4))));

    private static bool IsPhaseRecordMarker(int marker) =>
        marker is PhaseRecordMarker or PhaseRecordMarkerCompact;

    private static bool IsCurveRecordMarker(int marker) =>
        marker is CurveRecordMarker or CurveRecordMarkerCompact;

    private static bool IsSplineRecordMarker(int marker) =>
        marker is SplineRecordMarker or SplineRecordMarkerCompact;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}