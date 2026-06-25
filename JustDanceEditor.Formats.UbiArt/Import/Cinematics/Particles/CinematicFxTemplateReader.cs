using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Particles;

using System.Buffers.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class CinematicFxTemplateReader
{
    private const int MaxReasonableFxControls = 128;
    private const int MaxReasonableFxReferences = 128;
    private const int DescriptorNameToParticleParametersOffset = 0x3C;
    private const int FxDescriptorInputCount = 8;
    private const int ProceduralInputObjectMarker = 0x20;
    private const int ProceduralInputRecordStride = 0x28;
    private const int ProceduralInputFieldsOffset = 0x04;
    private const int ProceduralInputFloatFieldsOffset = 0x08;
    private const int ProceduralInputBoolFieldsOffset = 0x18;
    private const int FxDescriptorRuntimeFieldsSize = 0x0C;
    private const float MaxReasonableDescriptorDelaySeconds = 600.0f;

    public static bool TryRead(
        byte[] bytes,
        IReadOnlyList<string> texturePaths,
        IReadOnlyList<string> materialPaths,
        out CinematicFxTemplate template)
    {
        template = default!;
        if (texturePaths.Count == 0)
            return false;

        IReadOnlyList<CinematicParticleTemplate> particleTemplates =
            CinematicParticleTemplateReader.ReadAll(bytes, allowEmptyPhaseList: true);
        if (particleTemplates.Count == 0)
            return false;

        int emitterCount = Math.Min(texturePaths.Count, particleTemplates.Count);
        if (emitterCount == 0)
            return false;

        IReadOnlyList<CinematicFxControlTemplate> controls = ReadFxControls(bytes, out uint triggerFxNameId, out uint defaultFxNameId);
        IReadOnlyDictionary<uint, CinematicFxControlTemplate> controlsByNameId = controls.ToDictionary(control => control.NameId);
        List<CinematicFxEmitterTemplate> emitters = new(emitterCount);
        string? fallbackMaterialPath = materialPaths.Count > 0 ? materialPaths[0] : null;
        for (int index = 0; index < emitterCount; index++)
        {
            CinematicParticleTemplate particleTemplate = particleTemplates[index];
            string texturePath = texturePaths[index];
            string? materialPath = index < materialPaths.Count
                ? materialPaths[index]
                : fallbackMaterialPath;
            int? nextParticleParametersOffset = index + 1 < particleTemplates.Count
                ? particleTemplates[index + 1].Parameters.SourceOffset
                : null;
            FxDescriptorRuntimeFields runtimeFields = TryReadDescriptorRuntimeFields(
                bytes,
                particleTemplate.Parameters.SourceOffset,
                nextParticleParametersOffset,
                out FxDescriptorRuntimeFields parsedRuntimeFields)
                    ? parsedRuntimeFields
                    : FxDescriptorRuntimeFields.Default;
            emitters.Add(new CinematicFxEmitterTemplate(
                index,
                TryReadDescriptorNameId(bytes, particleTemplate.Parameters.SourceOffset, out uint descriptorNameId)
                    ? descriptorNameId
                    : 0,
                runtimeFields.AngleOffsetRadians,
                runtimeFields.MinDelaySeconds,
                runtimeFields.MaxDelaySeconds,
                Path.GetFileNameWithoutExtension(texturePath),
                texturePath,
                materialPath,
                particleTemplate));
        }

        ApplyFxControlRenderPriorities(emitters, controls);

        template = new CinematicFxTemplate(emitters, controls, controlsByNameId, defaultFxNameId, triggerFxNameId);
        return true;
    }

    private static void ApplyFxControlRenderPriorities(
        List<CinematicFxEmitterTemplate> emitters,
        IReadOnlyList<CinematicFxControlTemplate> controls)
    {
        if (emitters.Count == 0 || controls.Count == 0)
            return;

        Dictionary<uint, int> emitterByDescriptorNameId = [];
        for (int index = 0; index < emitters.Count; index++)
        {
            uint descriptorNameId = emitters[index].DescriptorNameId;
            if (CinematicFxIds.IsValid(descriptorNameId) &&
                !emitterByDescriptorNameId.ContainsKey(descriptorNameId))
            {
                emitterByDescriptorNameId[descriptorNameId] = index;
            }
        }

        // Particle Z sort is neutralized by assigning render priority in
        // particle-list order: index - params.positionOffset.z, with index
        // increasing by 0.001.
        float renderPriorityIndex = 0.0f;
        foreach (CinematicFxControlTemplate control in controls)
        {
            foreach (uint particleNameId in control.ParticleNameIds)
            {
                if (!emitterByDescriptorNameId.TryGetValue(particleNameId, out int emitterIndex))
                    continue;

                CinematicFxEmitterTemplate emitter = emitters[emitterIndex];
                CinematicParticleTemplate particleTemplate = emitter.ParticleTemplate;
                CinematicParticleParameters parameters = particleTemplate.Parameters;
                float renderPriority = renderPriorityIndex - parameters.PositionOffset.Z;
                emitters[emitterIndex] = emitter with
                {
                    ParticleTemplate = particleTemplate with
                    {
                        Parameters = parameters with { RenderPriority = renderPriority }
                    }
                };
                renderPriorityIndex += 0.001f;
            }
        }
    }

    private static bool TryReadDescriptorNameId(byte[] bytes, int parameterOffset, out uint nameId)
    {
        nameId = 0;
        int descriptorNameOffset = parameterOffset - DescriptorNameToParticleParametersOffset;
        if (descriptorNameOffset < 0 || descriptorNameOffset + 4 > bytes.Length)
            return false;

        uint value = ReadUInt32(bytes, descriptorNameOffset);
        if (!CinematicFxIds.IsValid(value))
            return false;

        nameId = value;
        return true;
    }

    private static bool TryReadDescriptorRuntimeFields(
        byte[] bytes,
        int parameterOffset,
        int? nextParameterOffset,
        out FxDescriptorRuntimeFields fields)
    {
        fields = FxDescriptorRuntimeFields.Default;
        int descriptorStart = parameterOffset - DescriptorNameToParticleParametersOffset;
        if (descriptorStart < 0 || descriptorStart >= bytes.Length)
            return false;

        int descriptorEnd = nextParameterOffset is { } nextOffset
            ? nextOffset - DescriptorNameToParticleParametersOffset
            : bytes.Length;
        descriptorEnd = Math.Clamp(descriptorEnd, descriptorStart, bytes.Length);
        if (descriptorEnd - descriptorStart < FxDescriptorRuntimeFieldsSize + (FxDescriptorInputCount * ProceduralInputRecordStride))
            return false;

        bool found = false;
        FxDescriptorRuntimeFields candidate = FxDescriptorRuntimeFields.Default;
        int lastPossibleInputRun = descriptorEnd - (FxDescriptorInputCount * ProceduralInputRecordStride);
        for (int inputRunOffset = descriptorStart + 4; inputRunOffset <= lastPossibleInputRun; inputRunOffset++)
        {
            if (!IsProceduralInputRun(bytes, inputRunOffset, descriptorEnd))
                continue;

            int runtimeFieldsOffset = inputRunOffset - FxDescriptorRuntimeFieldsSize;
            if (runtimeFieldsOffset < descriptorStart)
                continue;

            float angleOffsetRadians = ReadSingle(bytes, runtimeFieldsOffset);
            float minDelaySeconds = ReadSingle(bytes, runtimeFieldsOffset + 4);
            float maxDelaySeconds = ReadSingle(bytes, runtimeFieldsOffset + 8);
            if (!IsReasonableDescriptorRuntimeFields(angleOffsetRadians, minDelaySeconds, maxDelaySeconds))
                continue;

            candidate = new FxDescriptorRuntimeFields(angleOffsetRadians, minDelaySeconds, maxDelaySeconds);
            found = true;
        }

        fields = candidate;
        return found;
    }

    private static bool IsProceduralInputRun(byte[] bytes, int offset, int descriptorEnd)
    {
        for (int index = 0; index < FxDescriptorInputCount; index++)
        {
            int recordOffset = offset + (index * ProceduralInputRecordStride);
            if (recordOffset < 0 || recordOffset + ProceduralInputRecordStride > descriptorEnd)
                return false;

            if (ReadInt32(bytes, recordOffset) != ProceduralInputObjectMarker)
                return false;

            for (int floatOffset = recordOffset + ProceduralInputFloatFieldsOffset;
                 floatOffset < recordOffset + ProceduralInputBoolFieldsOffset;
                 floatOffset += 4)
            {
                if (!float.IsFinite(ReadSingle(bytes, floatOffset)))
                    return false;
            }

            for (int boolOffset = recordOffset + ProceduralInputBoolFieldsOffset;
                 boolOffset < recordOffset + ProceduralInputRecordStride;
                 boolOffset += 4)
            {
                uint value = ReadUInt32(bytes, boolOffset);
                if (value > 1)
                    return false;
            }
        }

        return true;
    }

    private static bool IsReasonableDescriptorRuntimeFields(
        float angleOffsetRadians,
        float minDelaySeconds,
        float maxDelaySeconds)
    {
        return float.IsFinite(angleOffsetRadians) &&
               Math.Abs(angleOffsetRadians) <= MathF.PI * 16.0f &&
               float.IsFinite(minDelaySeconds) &&
               float.IsFinite(maxDelaySeconds) &&
               minDelaySeconds >= 0.0f &&
               maxDelaySeconds >= 0.0f &&
               minDelaySeconds <= MaxReasonableDescriptorDelaySeconds &&
               maxDelaySeconds <= MaxReasonableDescriptorDelaySeconds &&
               maxDelaySeconds + 0.0001f >= minDelaySeconds;
    }

    private static IReadOnlyList<CinematicFxControlTemplate> ReadFxControls(
        byte[] bytes,
        out uint triggerFxNameId,
        out uint defaultFxNameId)
    {
        triggerFxNameId = CinematicFxIds.Invalid;
        defaultFxNameId = CinematicFxIds.Invalid;
        int componentOffset = FindUInt32(bytes, LegacyBinarySerializer.GetTypeId<CinematicFxControllerComponentTemplateBinary>());
        if (componentOffset < 0)
            return [];

        int nextComponentOffset = FindNextComponentOffset(bytes, componentOffset + 4);
        int componentEnd = nextComponentOffset >= 0 ? nextComponentOffset : bytes.Length;
        if (TryReadFxControllerComponent(bytes, componentOffset, componentEnd, out IReadOnlyList<CinematicFxControlTemplate> componentControls, out triggerFxNameId, out defaultFxNameId))
            return componentControls;

        List<CinematicFxControlTemplate> controls = [];
        for (int offset = componentOffset + 8; offset + 8 < componentEnd; offset += 4)
        {
            uint count = ReadUInt32(bytes, offset);
            if (count is 0 or > MaxReasonableFxControls)
                continue;

            if (!TryReadControlList(bytes, offset + 4, componentEnd, (int)count, controls, out int endOffset))
                continue;

            if (endOffset + 8 <= componentEnd)
            {
                triggerFxNameId = ReadUInt32(bytes, endOffset);
                defaultFxNameId = ReadUInt32(bytes, endOffset + 4);
            }

            return controls;
        }

        return controls;
    }

    private static bool TryReadFxControllerComponent(
        byte[] bytes,
        int componentOffset,
        int componentEnd,
        out IReadOnlyList<CinematicFxControlTemplate> controls,
        out uint triggerFxNameId,
        out uint defaultFxNameId)
    {
        controls = [];
        triggerFxNameId = CinematicFxIds.Invalid;
        defaultFxNameId = CinematicFxIds.Invalid;

        int cursor = componentOffset + 8;
        if (!TryReadIdList(bytes, ref cursor, componentEnd, out _))
            return false;

        if (cursor + 4 > componentEnd)
            return false;

        uint count = ReadUInt32(bytes, cursor);
        cursor += 4;
        if (count is 0 or > MaxReasonableFxControls)
            return false;

        List<CinematicFxControlTemplate> parsed = [];
        if (!TryReadControlList(bytes, cursor, componentEnd, (int)count, parsed, out int endOffset))
            return false;

        if (endOffset + 4 <= componentEnd)
        {
            uint trigger = ReadUInt32(bytes, endOffset);
            if (CinematicFxIds.IsValid(trigger))
                triggerFxNameId = trigger;
        }

        if (endOffset + 8 <= componentEnd)
        {
            uint defaultFx = ReadUInt32(bytes, endOffset + 4);
            if (CinematicFxIds.IsValid(defaultFx))
                defaultFxNameId = defaultFx;
        }

        controls = parsed;
        return controls.Count > 0;
    }

    private static bool TryReadControlList(
        byte[] bytes,
        int offset,
        int componentEnd,
        int count,
        List<CinematicFxControlTemplate> controls,
        out int endOffset)
    {
        endOffset = offset;
        int cursor = offset;
        List<CinematicFxControlTemplate> parsed = [];
        HashSet<uint> parsedNames = [];
        for (int index = 0; index < count; index++)
        {
            int recordStart = cursor;
            if (recordStart + 44 > componentEnd)
                return false;

            int recordSize = ReadInt32(bytes, recordStart);
            if (recordSize < 0x30 || recordSize > 0x400 || recordStart + 4 + recordSize > componentEnd + 0x40)
                return false;

            cursor = recordStart + 4;
            uint nameId = ReadUInt32(bytes, cursor);
            if (!CinematicFxIds.IsValid(nameId))
                return false;
            if (!parsedNames.Add(nameId))
                return false;

            cursor += 4;
            bool stopOnEndAnim = ReadSerializedBool(bytes, ref cursor, out bool validBool);
            if (!validBool)
                return false;
            bool playOnce = ReadSerializedBool(bytes, ref cursor, out validBool);
            if (!validBool)
                return false;
            bool emitFromBase = ReadSerializedBool(bytes, ref cursor, out validBool);
            if (!validBool)
                return false;
            bool useActorSpeed = ReadSerializedBool(bytes, ref cursor, out validBool);
            if (!validBool)
                return false;
            bool useActorOrientation = ReadSerializedBool(bytes, ref cursor, out validBool);
            if (!validBool)
                return false;
            bool useActorAlpha = ReadSerializedBool(bytes, ref cursor, out validBool);
            if (!validBool)
                return false;

            cursor += 4; // fxBoneName
            uint useBoneOrientation = ReadUInt32(bytes, cursor);
            if (useBoneOrientation > 2)
                return false;
            cursor += 4;

            if (!TryReadIdList(bytes, ref cursor, componentEnd, out _))
                return false;

            if (!TryReadIdList(bytes, ref cursor, componentEnd, out IReadOnlyList<uint> particles))
                return false;

            if (cursor + 16 <= bytes.Length)
                cursor += 16;

            parsed.Add(new CinematicFxControlTemplate(
                nameId,
                stopOnEndAnim,
                playOnce,
                emitFromBase,
                useActorSpeed,
                useActorOrientation,
                useActorAlpha,
                useBoneOrientation,
                particles));

            int declaredEnd = recordStart + 4 + recordSize;
            if (declaredEnd > cursor && declaredEnd <= componentEnd &&
                !LooksLikeFxControlRecord(bytes, cursor, componentEnd))
            {
                cursor = declaredEnd;
            }
        }

        controls.AddRange(parsed);

        endOffset = cursor;
        return controls.Count > 0;
    }

    private static bool LooksLikeFxControlRecord(byte[] bytes, int offset, int componentEnd)
    {
        if (offset + 44 > componentEnd)
            return false;

        int recordSizeOrFrameCount = ReadInt32(bytes, offset);
        if (recordSizeOrFrameCount < 0 || recordSizeOrFrameCount > 0x400)
            return false;

        uint nameId = ReadUInt32(bytes, offset + 4);
        if (!CinematicFxIds.IsValid(nameId))
            return false;

        for (int boolOffset = offset + 8; boolOffset <= offset + 28; boolOffset += 4)
        {
            uint value = ReadUInt32(bytes, boolOffset);
            if (value > 1)
                return false;
        }

        uint useBoneOrientation = ReadUInt32(bytes, offset + 36);
        if (useBoneOrientation > 2)
            return false;

        uint soundCount = ReadUInt32(bytes, offset + 40);
        if (soundCount > MaxReasonableFxReferences)
            return false;

        int particleCountOffset = offset + 44 + ((int)soundCount * 4);
        if (particleCountOffset + 4 > componentEnd)
            return false;

        uint particleCount = ReadUInt32(bytes, particleCountOffset);
        return particleCount <= MaxReasonableFxReferences &&
               particleCountOffset + 4 + (particleCount * 4) <= componentEnd + 0x40;
    }

    private static bool ReadSerializedBool(byte[] bytes, ref int cursor, out bool valid)
    {
        valid = false;
        if (cursor + 4 > bytes.Length)
            return false;

        uint value = ReadUInt32(bytes, cursor);
        cursor += 4;
        if (value > 1)
            return false;

        valid = true;
        return value != 0;
    }

    private static bool TryReadIdList(byte[] bytes, ref int cursor, int componentEnd, out IReadOnlyList<uint> ids)
    {
        ids = [];
        if (cursor + 4 > componentEnd)
            return false;

        uint count = ReadUInt32(bytes, cursor);
        cursor += 4;
        if (count > MaxReasonableFxReferences || cursor + (count * 4) > componentEnd + 0x40)
            return false;

        List<uint> values = new((int)count);
        for (int i = 0; i < count; i++)
        {
            uint id = ReadUInt32(bytes, cursor);
            cursor += 4;
            if (CinematicFxIds.IsValid(id))
                values.Add(id);
        }

        ids = values;
        return true;
    }

    private static int FindNextComponentOffset(byte[] bytes, int startOffset)
    {
        int fxBankOffset = FindUInt32(bytes, LegacyBinarySerializer.GetTypeId<CinematicFxBankComponentTemplateBinary>(), startOffset);
        int particleOffset = FindUInt32(bytes, LegacyBinarySerializer.GetTypeId<CinematicParticleGeneratorComponentTemplateBinary>(), startOffset);
        int next = -1;
        if (fxBankOffset >= 0)
            next = fxBankOffset;
        if (particleOffset >= 0 && (next < 0 || particleOffset < next))
            next = particleOffset;

        return next;
    }

    private static int FindUInt32(byte[] bytes, uint value, int startOffset = 0)
    {
        for (int offset = Math.Max(0, startOffset); offset <= bytes.Length - 4; offset += 4)
        {
            if (ReadUInt32(bytes, offset) == value)
                return offset;
        }

        return -1;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));

    private static float ReadSingle(byte[] bytes, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(bytes, offset));

    private readonly record struct FxDescriptorRuntimeFields(
        float AngleOffsetRadians,
        float MinDelaySeconds,
        float MaxDelaySeconds)
    {
        public static FxDescriptorRuntimeFields Default { get; } = new(0.0f, 0.0f, 0.0f);
    }
}