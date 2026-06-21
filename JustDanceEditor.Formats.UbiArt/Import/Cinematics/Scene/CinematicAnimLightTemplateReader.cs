using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicAnimLightTemplateReader
{
    private static readonly uint[] SubAnimRecordSizes = [0x70, 0xA8];
    private static readonly uint[] AnimPathAabbRecordSizes = [0x34, 0x6C];
    private const uint InvalidStringId = uint.MaxValue;

    internal static bool TryRead(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        ILogger logger,
        [NotNullWhen(true)] out CinematicAnimLightTemplate? template)
    {
        template = null;
        CinematicBinaryReader reader = new(bytes);
        Dictionary<uint, string> subAnimPathsByName = [];
        Dictionary<string, RenderGeometry> geometriesByPath = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, uint> aabbNamesByPath = new(StringComparer.OrdinalIgnoreCase);
        string? skeletonPath = null;
        string? patchBankPath = null;
        Dictionary<uint, string> patchBankPathsByName = [];
        Dictionary<uint, string> texturePathsByPatchBankName = [];
        uint pendingPatchBankNameId = 0;

        int nextPathSearchOffset = 0;
        for (int offset = 0; offset <= bytes.Length - 8; offset++)
        {
            if (offset < nextPathSearchOffset)
                continue;

            if (!CinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = CinematicNames.NormalizePath(candidate);
            if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                continue;

            string extension = Path.GetExtension(normalized);
            uint recordSize = ReadUInt32OrDefault(bytes, offset - 8);
            uint nameId = ReadUInt32OrDefault(bytes, offset - 4);
            bool consumedResourcePath = false;
            if (extension.Equals(".anm", StringComparison.OrdinalIgnoreCase))
            {
                if (SubAnimRecordSizes.Contains(recordSize) && CinematicFxIds.IsValid(nameId))
                {
                    subAnimPathsByName[nameId] = normalized;
                    consumedResourcePath = true;
                }
                else if (AnimPathAabbRecordSizes.Contains(recordSize) &&
                    TryReadAnimPathAabbGeometry(bytes, candidateEndOffset, out RenderGeometry? geometry))
                {
                    geometriesByPath[normalized] = geometry;
                    if (CinematicFxIds.IsValid(nameId))
                        aabbNamesByPath[normalized] = nameId;
                    consumedResourcePath = true;
                }
            }
            else if (skeletonPath == null && extension.Equals(".skl", StringComparison.OrdinalIgnoreCase))
            {
                skeletonPath = normalized;
                consumedResourcePath = true;
            }
            else if (extension.Equals(".pbk", StringComparison.OrdinalIgnoreCase))
            {
                patchBankPath ??= normalized;
                if (CinematicFxIds.IsValid(nameId))
                {
                    patchBankPathsByName[nameId] = normalized;
                    pendingPatchBankNameId = nameId;
                }

                consumedResourcePath = true;
            }
            else if (pendingPatchBankNameId != 0 &&
                CinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
            {
                texturePathsByPatchBankName[pendingPatchBankNameId] = normalized;
                pendingPatchBankNameId = 0;
                consumedResourcePath = true;
            }

            if (consumedResourcePath)
                nextPathSearchOffset = candidateEndOffset;
        }

        AddInferredPatchBankTexturePaths(fileSystem, patchBankPathsByName, texturePathsByPatchBankName);

        Dictionary<uint, CinematicAnimLightAnimation> animationsByName = [];
        Dictionary<string, CinematicAnimLightAnimation> animationsByPath = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CinematicAnimLightTrack?> tracksByPath = new(StringComparer.OrdinalIgnoreCase);
        CinematicAnimLightRig? rig = TryReadRig(fileSystem, skeletonPath, patchBankPath, patchBankPathsByName, logger);
        foreach ((uint nameId, string animationPath) in subAnimPathsByName)
        {
            CinematicAnimLightTrack? track = TryReadAnimationTrackCached(fileSystem, animationPath, tracksByPath, logger);
            RenderGeometry geometry = geometriesByPath.TryGetValue(animationPath, out RenderGeometry? pathGeometry)
                ? pathGeometry
                : RenderGeometry.NoAtlasQuad;
            CinematicAnimLightAnimation animation = new(
                nameId,
                animationPath,
                GetAnimationDurationFrames(track, fileSystem, animationPath, logger),
                geometry,
                track);
            animationsByName[nameId] = animation;
            animationsByPath.TryAdd(animationPath, animation);
        }

        foreach ((string animationPath, RenderGeometry geometry) in geometriesByPath)
        {
            if (animationsByPath.ContainsKey(animationPath))
                continue;

            uint nameId = aabbNamesByPath.TryGetValue(animationPath, out uint aabbNameId)
                ? aabbNameId
                : InvalidStringId;
            CinematicAnimLightTrack? track = TryReadAnimationTrackCached(fileSystem, animationPath, tracksByPath, logger);
            CinematicAnimLightAnimation animation = new(
                nameId,
                animationPath,
                GetAnimationDurationFrames(track, fileSystem, animationPath, logger),
                geometry,
                track);
            animationsByPath[animationPath] = animation;
            if (CinematicFxIds.IsValid(nameId))
                animationsByName.TryAdd(nameId, animation);
        }

        if (animationsByName.Count == 0 && animationsByPath.Count == 0)
            return false;

        CinematicAnimLightAnimation defaultAnimation =
            animationsByName.Values.FirstOrDefault() ??
            animationsByPath.Values.First();
        template = new CinematicAnimLightTemplate(
            defaultAnimation.NameId,
            animationsByName,
            animationsByPath,
            defaultAnimation.Geometry,
            skeletonPath,
            patchBankPath,
            texturePathsByPatchBankName,
            rig);
        return true;
    }

    private static void AddInferredPatchBankTexturePaths(
        JustDanceUbiArtFileSystem fileSystem,
        IReadOnlyDictionary<uint, string> patchBankPathsByName,
        IDictionary<uint, string> texturePathsByPatchBankName)
    {
        foreach ((uint nameId, string patchBankPath) in patchBankPathsByName)
        {
            if (texturePathsByPatchBankName.ContainsKey(nameId))
                continue;

            if (TryInferPatchBankTexturePath(fileSystem, patchBankPath, out string? texturePath))
                texturePathsByPatchBankName[nameId] = texturePath;
        }
    }

    private static bool TryInferPatchBankTexturePath(
        JustDanceUbiArtFileSystem fileSystem,
        string patchBankPath,
        [NotNullWhen(true)] out string? texturePath)
    {
        texturePath = null;
        if (string.IsNullOrWhiteSpace(patchBankPath))
            return false;

        string stem = patchBankPath.EndsWith(".pbk", StringComparison.OrdinalIgnoreCase)
            ? patchBankPath[..^4]
            : Path.ChangeExtension(patchBankPath, null);
        foreach (string extension in new[] { ".tga", ".png", ".dds" })
        {
            string candidate = $"{stem}{extension}";
            if (!TryGetCookedFile(fileSystem, candidate, out _))
                continue;

            texturePath = candidate;
            return true;
        }

        return false;
    }

    private static bool TryReadAnimPathAabbGeometry(
        byte[] bytes,
        int pathEndOffset,
        [NotNullWhen(true)] out RenderGeometry? geometry)
    {
        geometry = null;
        int markerOffset = pathEndOffset + 4;
        if (markerOffset < 0 || markerOffset + 20 > bytes.Length)
            return false;

        if (ReadUInt32OrDefault(bytes, markerOffset) != 0x10)
            return false;

        float minX = ReadSingle(bytes, markerOffset + 4);
        float minY = ReadSingle(bytes, markerOffset + 8);
        float maxX = ReadSingle(bytes, markerOffset + 12);
        float maxY = ReadSingle(bytes, markerOffset + 16);

        // GenAnim AABBs are stored with swapped Y bounds.
        geometry = RenderGeometry.FromAabb(minX, -maxY, maxX, -minY, CinematicGeometrySource.AnimLightBounds);
        return geometry.Source == CinematicGeometrySource.AnimLightBounds;
    }

    private static CinematicAnimLightRig? TryReadRig(
        JustDanceUbiArtFileSystem fileSystem,
        string? skeletonPath,
        string? defaultPatchBankPath,
        IReadOnlyDictionary<uint, string> patchBankPathsByName,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(skeletonPath) ||
            (string.IsNullOrWhiteSpace(defaultPatchBankPath) && patchBankPathsByName.Count == 0))
        {
            return null;
        }

        try
        {
            if (!TryReadSkeleton(fileSystem, skeletonPath, out CinematicAnimLightSkeleton? skeleton))
                return null;

            Dictionary<string, CinematicAnimLightPatchBank> patchBanksByPath = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<uint, CinematicAnimLightPatchBank> patchBanksByNameId = [];
            CinematicAnimLightPatchBank? defaultPatchBank = null;

            bool TryLoadPatchBank(string path, [NotNullWhen(true)] out CinematicAnimLightPatchBank? patchBank)
            {
                if (patchBanksByPath.TryGetValue(path, out patchBank))
                    return true;

                if (!TryReadPatchBank(fileSystem, path, skeleton, out patchBank))
                    return false;

                patchBanksByPath[path] = patchBank;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(defaultPatchBankPath) &&
                TryLoadPatchBank(defaultPatchBankPath, out CinematicAnimLightPatchBank? loadedDefaultPatchBank))
            {
                defaultPatchBank = loadedDefaultPatchBank;
            }

            foreach ((uint nameId, string path) in patchBankPathsByName)
            {
                if (!CinematicFxIds.IsValid(nameId) ||
                    !TryLoadPatchBank(path, out CinematicAnimLightPatchBank? namedPatchBank))
                {
                    continue;
                }

                patchBanksByNameId[nameId] = namedPatchBank;
                defaultPatchBank ??= namedPatchBank;
            }

            if (defaultPatchBank == null)
                return null;

            return new CinematicAnimLightRig(skeleton, defaultPatchBank, patchBanksByNameId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Cinematic AnimLight rig '{SkeletonPath}' could not be read.", skeletonPath);
            return null;
        }
    }

    private static bool TryReadSkeleton(
        JustDanceUbiArtFileSystem fileSystem,
        string skeletonPath,
        [NotNullWhen(true)] out CinematicAnimLightSkeleton? skeleton)
    {
        skeleton = null;
        if (!TryGetCookedFile(fileSystem, skeletonPath, out CookedFile? file))
            return false;

        CinematicBinaryReader reader = new(CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, file));
        if (reader.Remaining < 4)
            return false;

        reader.SkipUInt32();
        IReadOnlyDictionary<uint, int> boneIndicesByNameId = ReadKeyArrayInt32(reader);
        ReadKeyArrayInt32(reader);
        ReadKeyArrayInt32(reader);
        List<RawAnimLightBone> rawBones = ReadRawBones(reader);
        IReadOnlyList<CinematicAnimLightBoneState> tPose = ReadBoneStates(reader);
        if (rawBones.Count == 0 || tPose.Count != rawBones.Count)
            return false;

        Dictionary<uint, int> linkToBoneIndex = new(rawBones.Count);
        for (int i = 0; i < rawBones.Count; i++)
            linkToBoneIndex[rawBones[i].LinkId] = i;

        CinematicAnimLightBone[] bones = new CinematicAnimLightBone[rawBones.Count];
        for (int i = 0; i < rawBones.Count; i++)
        {
            RawAnimLightBone raw = rawBones[i];
            int parentIndex = raw.ParentLinkId != 0 && linkToBoneIndex.TryGetValue(raw.ParentLinkId, out int resolvedParent)
                ? resolvedParent
                : -1;
            bones[i] = new CinematicAnimLightBone(raw.NameId, parentIndex);
        }

        skeleton = new CinematicAnimLightSkeleton(
            bones,
            tPose,
            boneIndicesByNameId,
            ComputeBoneOrder(bones, boneIndicesByNameId));
        return true;
    }

    private static bool TryReadPatchBank(
        JustDanceUbiArtFileSystem fileSystem,
        string patchBankPath,
        CinematicAnimLightSkeleton skeleton,
        [NotNullWhen(true)] out CinematicAnimLightPatchBank? patchBank)
    {
        patchBank = null;
        if (!TryGetCookedFile(fileSystem, patchBankPath, out CookedFile? file))
            return false;

        CinematicBinaryReader reader = new(CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, file));
        if (reader.Remaining < 12)
            return false;

        reader.SkipUInt32();
        reader.SkipUInt32();
        float textureRatio = reader.ReadSingle();
        IReadOnlyDictionary<uint, int> templateIndicesByNameId = ReadKeyArrayInt32(reader);
        int templateCount = ReadArrayCount(reader, 1024);
        List<CinematicAnimLightPatchTemplate> templates = new(templateCount);
        for (int i = 0; i < templateCount; i++)
            templates.Add(ReadPatchTemplate(reader, skeleton));

        patchBank = new CinematicAnimLightPatchBank(textureRatio, templateIndicesByNameId, templates);
        return templates.Count > 0;
    }

    private static CinematicAnimLightPatchTemplate ReadPatchTemplate(
        CinematicBinaryReader reader,
        CinematicAnimLightSkeleton skeleton)
    {
        ReadKeyArrayInt32(reader);
        List<RawAnimLightBone> rawBones = ReadRawBones(reader);
        IReadOnlyList<CinematicAnimLightBoneState> templatePose = ReadBoneStates(reader);

        Dictionary<uint, int> boneLinks = new(rawBones.Count);
        for (int i = 0; i < rawBones.Count; i++)
            boneLinks[rawBones[i].LinkId] = i;

        CinematicAnimLightPatchBone[] bones = new CinematicAnimLightPatchBone[rawBones.Count];
        for (int i = 0; i < rawBones.Count; i++)
        {
            RawAnimLightBone raw = rawBones[i];
            int mainSkeletonIndex = skeleton.BoneIndicesByNameId.TryGetValue(raw.NameId, out int resolvedIndex)
                ? resolvedIndex
                : -1;
            float templateScaleX = i < templatePose.Count ? templatePose[i].Scale.X : 1.0f;
            float boneRatio = mainSkeletonIndex >= 0 &&
                mainSkeletonIndex < skeleton.TPose.Count &&
                Math.Abs(templateScaleX) > 0.000001f
                    ? skeleton.TPose[mainSkeletonIndex].Scale.X / templateScaleX
                    : 1.0f;
            bones[i] = new CinematicAnimLightPatchBone(raw.NameId, mainSkeletonIndex, boneRatio);
        }

        int patchPointCount = ReadArrayCount(reader, 4096);
        List<CinematicAnimLightPatchPoint> patchPoints = new(patchPointCount);
        Dictionary<uint, int> patchPointLinks = new(patchPointCount);
        for (int i = 0; i < patchPointCount; i++)
        {
            uint linkId = reader.ReadUInt32();
            int pointIndex = (int)reader.ReadUInt32();
            Vector2 positionUv = ReadVector2(reader);
            Vector2 normalUv = ReadVector2(reader);
            uint bonePtr = reader.ReadUInt32();
            Vector2 localPosition = ReadVector2(reader);
            Vector2 localNormal = ReadVector2(reader);
            int boneIndex = boneLinks.TryGetValue(bonePtr, out int resolvedBone)
                ? resolvedBone
                : -1;
            patchPointLinks[linkId] = patchPoints.Count;
            patchPoints.Add(new CinematicAnimLightPatchPoint(
                pointIndex >= 0 ? pointIndex : patchPoints.Count,
                positionUv,
                normalUv,
                boneIndex,
                localPosition,
                localNormal));
        }

        int patchCount = ReadArrayCount(reader, 4096);
        List<CinematicAnimLightPatch> patches = new(patchCount);
        for (int i = 0; i < patchCount; i++)
        {
            reader.SkipUInt32();
            reader.SkipUInt32();
            int pointCount = reader.ReadByte();
            if (pointCount is < 0 or > 4)
                throw new InvalidDataException($"Invalid AnimLight patch point count {pointCount} at 0x{reader.Offset - 1:X}.");

            int[] pointIndices = new int[pointCount];
            for (int point = 0; point < pointCount; point++)
            {
                uint pointLink = reader.ReadUInt32();
                pointIndices[point] = patchPointLinks.TryGetValue(pointLink, out int resolvedPoint)
                    ? resolvedPoint
                    : -1;
            }

            patches.Add(new CinematicAnimLightPatch(pointIndices));
        }

        return new CinematicAnimLightPatchTemplate(bones, patchPoints, patches);
    }

    private static CinematicAnimLightTrack? TryReadAnimationTrackCached(
        JustDanceUbiArtFileSystem fileSystem,
        string animationPath,
        Dictionary<string, CinematicAnimLightTrack?> tracksByPath,
        ILogger logger)
    {
        if (tracksByPath.TryGetValue(animationPath, out CinematicAnimLightTrack? cached))
            return cached;

        CinematicAnimLightTrack? track = TryReadAnimationTrack(fileSystem, animationPath, logger);
        tracksByPath[animationPath] = track;
        return track;
    }

    private static CinematicAnimLightTrack? TryReadAnimationTrack(
        JustDanceUbiArtFileSystem fileSystem,
        string animationPath,
        ILogger logger)
    {
        if (!TryGetCookedFile(fileSystem, animationPath, out CookedFile? file))
            return null;

        try
        {
            CinematicBinaryReader reader = new(CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, file));
            if (reader.Remaining < 8)
                return null;

            reader.SkipUInt32();
            float endFrame = reader.ReadSingle();

            int bmlCount = ReadArrayCount(reader, 4096);
            List<CinematicAnimLightBmlFrame> bmlFrames = new(bmlCount);
            for (int i = 0; i < bmlCount; i++)
            {
                float frame = reader.ReadSingle();
                int refCount = ReadArrayCount(reader, 1024);
                CinematicAnimLightTemplateRef[] refs = new CinematicAnimLightTemplateRef[refCount];
                for (int refIndex = 0; refIndex < refCount; refIndex++)
                    refs[refIndex] = new CinematicAnimLightTemplateRef(reader.ReadUInt32(), reader.ReadUInt32());

                bmlFrames.Add(new CinematicAnimLightBmlFrame(frame, refs));
            }

            int pasCount = ReadArrayCount(reader, 65536);
            CinematicAnimLightPasKey[] pasKeys = new CinematicAnimLightPasKey[pasCount];
            for (int i = 0; i < pasCount; i++)
            {
                pasKeys[i] = new CinematicAnimLightPasKey(
                    reader.ReadUInt16(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadUInt32());
            }

            int zalCount = ReadArrayCount(reader, 65536);
            CinematicAnimLightZalKey[] zalKeys = new CinematicAnimLightZalKey[zalCount];
            for (int i = 0; i < zalCount; i++)
            {
                zalKeys[i] = new CinematicAnimLightZalKey(
                    reader.ReadUInt16(),
                    reader.ReadSingle(),
                    reader.ReadInt16());
            }

            float maxAngle = reader.ReadSingle();
            float maxPosition = reader.ReadSingle();
            float maxScale = reader.ReadSingle();

            int polylineCount = ReadArrayCount(reader, 4096);
            for (int i = 0; i < polylineCount; i++)
            {
                reader.SkipSingle();
                int refCount = ReadArrayCount(reader, 4096);
                reader.Skip(refCount * 4);
            }

            int boneTrackCount = ReadArrayCount(reader, 4096);
            CinematicAnimLightBoneTrack[] boneTracks = new CinematicAnimLightBoneTrack[boneTrackCount];
            for (int i = 0; i < boneTrackCount; i++)
            {
                boneTracks[i] = new CinematicAnimLightBoneTrack(
                    reader.ReadUInt16(),
                    reader.ReadUInt16(),
                    reader.ReadUInt16(),
                    reader.ReadUInt16());
            }

            return new CinematicAnimLightTrack(
                endFrame,
                zalKeys.Any(key => key.Alpha != 0),
                bmlFrames,
                pasKeys,
                zalKeys,
                maxAngle,
                maxPosition,
                maxScale,
                boneTracks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Cinematic AnimLight animation track '{AnimationPath}' could not be read.", animationPath);
            return null;
        }
    }

    private static int GetAnimationDurationFrames(
        CinematicAnimLightTrack? track,
        JustDanceUbiArtFileSystem fileSystem,
        string animationPath,
        ILogger logger)
    {
        if (track != null &&
            float.IsFinite(track.EndFrame) &&
            track.EndFrame >= 0)
        {
            return Math.Max(0, (int)Math.Floor(track.EndFrame + 1.0f + 0.5f));
        }

        return TryReadAnimationDurationFrames(fileSystem, animationPath, logger);
    }

    private static int TryReadAnimationDurationFrames(
        JustDanceUbiArtFileSystem fileSystem,
        string animationPath,
        ILogger logger)
    {
        if (!TryGetCookedFile(fileSystem, animationPath, out CookedFile? animationFile))
            return 0;

        try
        {
            byte[] bytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, animationFile);
            if (bytes.Length < 8)
                return 0;

            float endFrame = ReadSingle(bytes, 4);
            if (!float.IsFinite(endFrame) || endFrame < 0)
                return 0;

            return Math.Max(0, (int)Math.Floor(endFrame + 1.0f + 0.5f));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Cinematic AnimLight animation '{AnimationPath}' could not be read.", animationPath);
            return 0;
        }
    }

    private static bool TryGetCookedFile(
        JustDanceUbiArtFileSystem fileSystem,
        string path,
        [NotNullWhen(true)] out CookedFile? file)
    {
        if (fileSystem.GetFilePath(path, out file))
            return true;

        if (!path.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) &&
            fileSystem.GetFilePath($"{path}.ckd", out file))
        {
            return true;
        }

        file = null;
        return false;
    }

    private static IReadOnlyDictionary<uint, int> ReadKeyArrayInt32(CinematicBinaryReader reader)
    {
        int keyCount = ReadArrayCount(reader, 65536);
        uint[] keys = new uint[keyCount];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = reader.ReadUInt32();

        int valueCount = ReadArrayCount(reader, 65536);
        if (valueCount != keyCount)
            throw new InvalidDataException($"AnimLight KeyArray key/value mismatch {keyCount}/{valueCount} at 0x{reader.Offset:X}.");

        Dictionary<uint, int> values = new(keyCount);
        for (int i = 0; i < valueCount; i++)
            values[keys[i]] = reader.ReadInt32();

        return values;
    }

    private static List<RawAnimLightBone> ReadRawBones(CinematicBinaryReader reader)
    {
        int count = ReadArrayCount(reader, 4096);
        List<RawAnimLightBone> bones = new(count);
        for (int i = 0; i < count; i++)
        {
            uint linkId = reader.ReadUInt32();
            uint nameId = reader.ReadUInt32();
            reader.SkipByte();
            int patchPointPointerCount = ReadArrayCount(reader, 4096);
            reader.Skip(patchPointPointerCount * 4);
            uint parentLinkId = reader.ReadUInt32();
            bones.Add(new RawAnimLightBone(linkId, nameId, parentLinkId));
        }

        return bones;
    }

    private static IReadOnlyList<CinematicAnimLightBoneState> ReadBoneStates(CinematicBinaryReader reader)
    {
        int count = ReadArrayCount(reader, 4096);
        CinematicAnimLightBoneState[] states = new CinematicAnimLightBoneState[count];
        for (int i = 0; i < states.Length; i++)
            states[i] = ReadBoneState(reader);

        return states;
    }

    private static CinematicAnimLightBoneState ReadBoneState(CinematicBinaryReader reader) =>
        new(
            ReadVector2(reader),
            reader.ReadSingle(),
            ReadVector2(reader),
            reader.ReadSingle(),
            reader.ReadSingle(),
            ReadVector2(reader),
            reader.ReadSingle());

    private static IReadOnlyList<int> ComputeBoneOrder(
        IReadOnlyList<CinematicAnimLightBone> bones,
        IReadOnlyDictionary<uint, int> boneIndicesByNameId)
    {
        bool[] done = new bool[bones.Count];
        List<int> order = new(bones.Count);
        for (int i = 0; i < bones.Count; i++)
        {
            if (done[i])
                continue;

            done[i] = true;
            int insertIndex = order.Count;
            order.Add(i);
            int parent = bones[i].ParentIndex;
            while (parent >= 0 && parent < bones.Count && !done[parent])
            {
                done[parent] = true;
                order.Insert(insertIndex, parent);
                parent = bones[parent].ParentIndex;
            }
        }

        const uint RootStringId = 0x0A22DD9C;
        if (boneIndicesByNameId.TryGetValue(RootStringId, out int rootIndex))
        {
            int orderIndex = order.IndexOf(rootIndex);
            if (orderIndex > 0)
            {
                order.RemoveAt(orderIndex);
                order.Insert(0, rootIndex);
            }
        }

        return order;
    }

    private static Vector2 ReadVector2(CinematicBinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    private static int ReadArrayCount(CinematicBinaryReader reader, int maxCount)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maxCount)
            throw new InvalidDataException($"Invalid AnimLight array count {count} at 0x{reader.Offset - 4:X}.");

        return count;
    }

    private static uint ReadUInt32OrDefault(byte[] bytes, int offset) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
            : 0;

    private static float ReadSingle(byte[] bytes, int offset)
    {
        uint raw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private readonly record struct RawAnimLightBone(
        uint LinkId,
        uint NameId,
        uint ParentLinkId);
}
