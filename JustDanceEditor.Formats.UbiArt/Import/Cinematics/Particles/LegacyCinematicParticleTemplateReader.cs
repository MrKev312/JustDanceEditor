using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

using System.Buffers.Binary;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleTemplateReader
{
    private const int AabbSerializedSize = 0x10;
    private const int PhaseRecordMarker = 0x54;
    private const int CurveRecordMarker = 0x48;
    private const int SplineRecordMarker = 0x24;
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

    public static bool TryRead(byte[] bytes, out LegacyCinematicParticleTemplate template)
    {
        template = default!;
        for (int offset = 0; offset <= bytes.Length - 4; offset++)
        {
            if (TryReadAt(bytes, offset, allowEmptyPhaseList: false, out template))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<LegacyCinematicParticleTemplate> ReadAll(byte[] bytes, bool allowEmptyPhaseList = false)
    {
        List<LegacyCinematicParticleTemplate> templates = [];
        HashSet<int> sourceOffsets = [];
        for (int offset = 0; offset <= bytes.Length - 4; offset++)
        {
            if (!TryReadAt(bytes, offset, allowEmptyPhaseList, out LegacyCinematicParticleTemplate? template) ||
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
        out LegacyCinematicParticleTemplate template)
    {
        template = default!;
        try
        {
            LegacyCinematicBinaryReader reader = new(bytes, offset);
            uint maxParticles = reader.ReadUInt32();
            if (maxParticles is 0 or > MaxReasonableParticleCount)
                return false;

            LegacyCinematicParticleColor defaultColor = ReadColor(reader);
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

            LegacyCinematicParticleParameters parameters = new(
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
            List<LegacyCinematicParticlePhase> phases = new((int)serializedPhaseCount);
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

            LegacyCinematicParticleCurves curves = ReadParticleCurves(bytes, reader.Offset, out int afterCurvesOffset);
            LegacyCinematicParticleModeTail modeTail = ReadModeTail(bytes, afterCurvesOffset);
            parameters = parameters with
            {
                GenerationType = modeTail.GenerationType,
                GeneratorMode = modeTail.GeneratorMode,
                GeneratorEmitMode = modeTail.GeneratorEmitMode
            };

            template = new LegacyCinematicParticleTemplate(
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
            if (marker != PhaseRecordMarker)
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
        if (nextObjectSize is not (0 or 0x48))
            return false;

        phaseListOffset = searchStartOffset;
        return true;
    }

    private static LegacyCinematicParticlePhase CreateDefaultPhase() =>
        new(
            1.0f,
            new LegacyCinematicParticleColor(1, 1, 1, 1),
            new LegacyCinematicParticleColor(1, 1, 1, 1),
            new Vector2(1, 1),
            new Vector2(1, 1),
            -1,
            -1,
            uint.MaxValue,
            0.0f,
            false,
            true);

    private static LegacyCinematicParticlePhase ReadPhase(LegacyCinematicBinaryReader reader) =>
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

    private static LegacyCinematicParticleColor ReadColor(LegacyCinematicBinaryReader reader)
    {
        float blue = reader.ReadSingle();
        float green = reader.ReadSingle();
        float red = reader.ReadSingle();
        float alpha = reader.ReadSingle();
        return new LegacyCinematicParticleColor(red, green, blue, alpha);
    }

    private static Vector2 ReadVector2(LegacyCinematicBinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    private static Vector3 ReadVector3(LegacyCinematicBinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static LegacyCinematicParticleBox ReadBox(LegacyCinematicBinaryReader reader)
    {
        int size = reader.ReadInt32();
        if (size != AabbSerializedSize)
            throw new InvalidDataException($"Invalid legacy particle box marker 0x{size:X8} at 0x{reader.Offset - 4:X}.");

        return new LegacyCinematicParticleBox(ReadVector2(reader), ReadVector2(reader));
    }

    private static bool ReadBool(LegacyCinematicBinaryReader reader, out bool valid)
    {
        uint raw = reader.ReadUInt32();
        valid = raw <= 1;
        return raw != 0;
    }

    private static bool IsNormalized(float value) =>
        value is >= -0.0001f and <= 1.0001f;

    private static LegacyCinematicParticleAnimation ReadAnimationTail(byte[] bytes, int parametersOffset)
    {
        // The animation tail sits immediately before the cooked params object.
        // Some files include the params object's four-byte size header before
        // the parameter payload this reader starts at.
        int offset = parametersOffset - TemplateAnimationTailSize - SerializedParamsObjectHeaderSize;
        if (TryReadAnimationTailAt(bytes, offset, out LegacyCinematicParticleAnimation animation))
            return animation;

        return TryReadAnimationTailAt(bytes, parametersOffset - TemplateAnimationTailSize, out animation)
            ? animation
            : LegacyCinematicParticleAnimation.Default;
    }

    private static bool TryReadAnimationTailAt(
        byte[] bytes,
        int offset,
        out LegacyCinematicParticleAnimation animation)
    {
        animation = LegacyCinematicParticleAnimation.Default;
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

        animation = new LegacyCinematicParticleAnimation(
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

    private static LegacyCinematicParticleCurves ReadParticleCurves(byte[] bytes, int offset, out int nextOffset)
    {
        if (!TryReadParticleCurves(bytes, offset, out LegacyCinematicParticleCurves curves, out nextOffset))
        {
            nextOffset = offset;
            return LegacyCinematicParticleCurves.Empty;
        }

        return curves;
    }

    private static bool TryReadParticleCurves(
        byte[] bytes,
        int offset,
        out LegacyCinematicParticleCurves curves,
        out int nextOffset)
    {
        curves = LegacyCinematicParticleCurves.Empty;
        nextOffset = offset;
        if (offset < 0 || offset >= bytes.Length)
            return false;

        LegacyCinematicParticleCurve position = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve angle = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve velocityMult = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve accelerationX = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve accelerationY = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve accelerationZ = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve atlasAnimation = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitAtlasAnimation = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve size = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve sizeY = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve alpha = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve rgb = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve rgb1 = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve rgb2 = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve rgb3 = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitVelocity = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitVelocityAngle = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitAngle = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitAngularSpeed = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve frequency = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve particleLifeTime = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitAlpha = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitColorFactor = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitSizeXy = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitAcceleration = LegacyCinematicParticleCurve.Empty;
        LegacyCinematicParticleCurve emitGravity = LegacyCinematicParticleCurve.Empty;
        int cursor = offset;
        for (int curveIndex = 0; curveIndex < SerializedParticleCurveCount; curveIndex++)
        {
            if (!TryReadParticleCurve(bytes, cursor, out LegacyCinematicParticleCurve curve, out int curveNextOffset))
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
        curves = new LegacyCinematicParticleCurves(
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

    private static LegacyCinematicParticleModeTail ReadModeTail(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 12 > bytes.Length)
            return LegacyCinematicParticleModeTail.Default;

        uint generationType = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        uint generatorMode = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 4, 4));
        uint emitMode = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8, 4));
        if (generationType is < ParticleGenerationPoints or > ParticleGenerationHemisphere ||
            generatorMode is < ParticleModeFollow or > ParticleModeManual ||
            emitMode is < ParticleEmitModeOverTime or > ParticleEmitModeOverDistance)
        {
            return LegacyCinematicParticleModeTail.Default;
        }

        return new LegacyCinematicParticleModeTail(generationType, generatorMode, emitMode);
    }

    private static bool TryReadParticleCurve(
        byte[] bytes,
        int offset,
        out LegacyCinematicParticleCurve curve,
        out int nextOffset)
    {
        curve = LegacyCinematicParticleCurve.Empty;
        nextOffset = offset;
        if (offset < 0 || offset + CurveFixedSize > bytes.Length)
            return false;

        if (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)) != CurveRecordMarker ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 32, 4)) != SplineRecordMarker)
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

        List<LegacyCinematicParticleCurvePoint> points = new((int)pointCount);
        int cursor = pointsOffset;
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            if (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor, 4)) != CurveRecordMarker)
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

            points.Add(new LegacyCinematicParticleCurvePoint(
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

        curve = new LegacyCinematicParticleCurve(baseTime, outputMin, outputMax, points);
        nextOffset = offset + serializedSize;
        return true;
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset) =>
        new(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8, 4))));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

internal sealed record LegacyCinematicParticleTemplate(
    LegacyCinematicParticleAnimation Animation,
    LegacyCinematicParticleParameters Parameters,
    IReadOnlyList<LegacyCinematicParticlePhase> Phases,
    LegacyCinematicParticleCurves Curves);

internal sealed record LegacyCinematicParticleAnimation(
    bool Loop,
    bool UseUvRandom,
    int StartAnimIndex,
    int EndAnimIndex,
    uint AnimName,
    float AnimUvFrequency)
{
    public static LegacyCinematicParticleAnimation Default { get; } = new(false, false, -1, -1, 0, 1.0f);

    public bool HasAtlasAnimation => StartAnimIndex >= 0 && EndAnimIndex >= 0;
}

internal sealed record LegacyCinematicParticleCurves(
    LegacyCinematicParticleCurve Position,
    LegacyCinematicParticleCurve Angle,
    LegacyCinematicParticleCurve VelocityMult,
    LegacyCinematicParticleCurve AccelerationX,
    LegacyCinematicParticleCurve AccelerationY,
    LegacyCinematicParticleCurve AccelerationZ,
    LegacyCinematicParticleCurve Size,
    LegacyCinematicParticleCurve SizeY,
    LegacyCinematicParticleCurve Alpha,
    LegacyCinematicParticleCurve Rgb,
    LegacyCinematicParticleCurve Rgb1,
    LegacyCinematicParticleCurve Rgb2,
    LegacyCinematicParticleCurve Rgb3,
    LegacyCinematicParticleCurve AtlasAnimation,
    LegacyCinematicParticleCurve EmitVelocity,
    LegacyCinematicParticleCurve EmitVelocityAngle,
    LegacyCinematicParticleCurve EmitAngle,
    LegacyCinematicParticleCurve EmitAngularSpeed,
    LegacyCinematicParticleCurve Frequency,
    LegacyCinematicParticleCurve ParticleLifeTime,
    LegacyCinematicParticleCurve EmitAlpha,
    LegacyCinematicParticleCurve EmitColorFactor,
    LegacyCinematicParticleCurve EmitSizeXy,
    LegacyCinematicParticleCurve EmitAcceleration,
    LegacyCinematicParticleCurve EmitGravity,
    LegacyCinematicParticleCurve EmitAtlasAnimation)
{
    public static LegacyCinematicParticleCurves Empty { get; } = new(
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty,
        LegacyCinematicParticleCurve.Empty);
}

internal sealed record LegacyCinematicParticleCurve(
    float BaseTime,
    Vector3 OutputMin,
    Vector3 OutputMax,
    IReadOnlyList<LegacyCinematicParticleCurvePoint> Points)
{
    public static LegacyCinematicParticleCurve Empty { get; } = new(
        0.0f,
        Vector3.Zero,
        Vector3.Zero,
        []);

    public bool IsSet => BaseTime > 0.0f && Points.Count > 0;
}

internal readonly record struct LegacyCinematicParticleCurvePoint(
    float Time,
    Vector3 Value,
    Vector3 NormalIn,
    Vector3 NormalInTime,
    Vector3 NormalOut,
    Vector3 NormalOutTime,
    int Interpolation);

internal sealed record LegacyCinematicParticleParameters(
    int SourceOffset,
    uint MaxParticles,
    LegacyCinematicParticleColor DefaultColor,
    uint EmitParticlesCount,
    bool ForceNoDynamicFog,
    bool RenderInReflection,
    float DieFadeTime,
    float EmitterMaxLifeTime,
    Vector3 PositionOffset,
    Vector2 Pivot,
    float VelocityNorm,
    float VelocityAngle,
    float VelocityAngleDelta,
    Vector3 Gravity,
    Vector3 Acceleration,
    float Depth,
    bool UseZAsDepth,
    float VelocityVar,
    float Friction,
    float Frequency,
    float FrequencyDelta,
    bool ForceEmitAtStart,
    uint EmitBatchCount,
    uint EmitBatchCountAllAtOnce,
    uint EmitBatchCountAllAtOnceMax,
    float InitAngle,
    float AngleDelta,
    float AngularSpeed,
    float AngularSpeedDelta,
    float TimeTarget,
    uint PhaseCount,
    float RenderPriority,
    float InitLifeTime,
    float CircleRadius,
    float InnerCircleRadius,
    Vector3 ScaleShape,
    Vector3 RotateShape,
    bool RandomizeDirection,
    uint FollowBezier,
    bool GetAtlasSize,
    LegacyCinematicParticleBox GenerationBox,
    float GenerationSize,
    uint GenerationSide,
    float GenerationBezierStart,
    float GenerationBezierEnd,
    float GenerationBezierDensity,
    LegacyCinematicParticleBox BoundingBox,
    uint OrientDirection,
    uint UvMode,
    uint UvModeFlags,
    float UniformScale,
    bool UseImpostor,
    bool ShowImpostorRender,
    uint ImpostorTextureSizeX,
    uint ImpostorTextureSizeY,
    float GenerationAngleMin,
    float GenerationAngleMax,
    bool CanFlipAngleOffset,
    bool CanFlipInitAngle,
    bool CanFlipAngularSpeed,
    bool CanFlipPivot,
    bool CanFlipPosition,
    bool CanFlipUv,
    bool CanFlipAngleMin,
    bool CanFlipAngleMax,
    bool CanFlipAcceleration,
    bool CanFlipOrientDirection,
    uint NumberSplit,
    float SplitDelta,
    uint UseMatrix,
    bool UsePhasesColorAndSize,
    bool UseActorTranslation,
    Vector2 ActorTranslationOffset,
    bool DisableLight,
    uint GenerationType = 0,
    uint GeneratorMode = 1,
    uint GeneratorEmitMode = 0);

internal sealed record LegacyCinematicParticlePhase(
    float PhaseTime,
    LegacyCinematicParticleColor ColorMin,
    LegacyCinematicParticleColor ColorMax,
    Vector2 SizeMin,
    Vector2 SizeMax,
    int AnimStart,
    int AnimEnd,
    uint AnimName,
    float DeltaPhaseTime,
    bool AnimStretchTime,
    bool BlendToNextPhase);

internal readonly record struct LegacyCinematicParticleColor(float Red, float Green, float Blue, float Alpha);

internal readonly record struct LegacyCinematicParticleBox(Vector2 Min, Vector2 Max);

internal readonly record struct LegacyCinematicParticleModeTail(
    uint GenerationType,
    uint GeneratorMode,
    uint GeneratorEmitMode)
{
    public static LegacyCinematicParticleModeTail Default { get; } = new(0, 1, 0);
}