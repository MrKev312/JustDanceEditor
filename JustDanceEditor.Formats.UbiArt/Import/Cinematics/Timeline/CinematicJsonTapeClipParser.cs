using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicJsonTapeClipParser
{
    internal static bool LooksLikeJson(byte[] bytes)
    {
        int index = 0;
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            index = 3;
        }

        while (index < bytes.Length && char.IsWhiteSpace((char)bytes[index]))
            index++;

        return index < bytes.Length && bytes[index] is (byte)'{' or (byte)'[';
    }

    internal static IEnumerable<TapeClip> ReadTapeClips(
        byte[] bytes,
        string tapePath,
        ILogger logger)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("Clips", out JsonElement clipsElement) ||
            clipsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        ActorTargetPath[] actorPaths = ReadActorPaths(root);
        foreach (JsonElement clipElement in clipsElement.EnumerateArray())
        {
            TapeClip? clip = CreateClip(clipElement, actorPaths, logger, tapePath);
            if (clip != null)
                yield return clip;
        }
    }

    private static TapeClip? CreateClip(
        JsonElement clip,
        IReadOnlyList<ActorTargetPath> actorPaths,
        ILogger logger,
        string tapePath)
    {
        if (ReadInt(clip, "IsActive", 1) == 0)
            return null;

        string className = ReadString(clip, "__class");
        if (string.IsNullOrWhiteSpace(className))
            return null;

        int startFrame = ReadInt(clip, "StartTime", 0);
        int durationFrames = ReadInt(clip, "Duration", 0);
        IReadOnlyList<ActorTargetPath> targets = ReadTargets(clip, actorPaths);

        return className switch
        {
            "TapeReferenceClip" => new TapeClip(
                LegacyBinarySerializer.GetTypeId<CinematicTapeReferenceClipBinary>(),
                startFrame,
                durationFrames,
                CinematicNames.NormalizePath(ReadString(clip, "Path")),
                [],
                [],
                LoopingType: (CinematicTapeReferenceLoopingType)ReadInt(clip, "Loop", 0)),
            "SoundSetClip" => new TapeClip(LegacyBinarySerializer.GetTypeId<CinematicSoundSetClipBinary>(), startFrame, durationFrames, CinematicNames.NormalizePath(ReadString(clip, "SoundSetPath")), [], []),
            "TapeLauncherClip" => CreateTapeLauncherClip(clip, startFrame, durationFrames, targets),
            "AlphaClip" => CreatePropertyClip<CinematicAlphaClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "Curve")]),
            "ActorEnableClip" => CreatePropertyClip<CinematicActorEnableClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [CinematicVisualStateBuilder.CreateConstantCurve(ReadInt(clip, "Active", 1) != 0 ? 1 : 0)]),
            "ColorClip" => CreatePropertyClip<CinematicMaterialColorClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [
                    ReadCurve(clip, "CurveRed") ?? ReadCurve(clip, "CurveR"),
                    ReadCurve(clip, "CurveGreen") ?? ReadCurve(clip, "CurveG"),
                    ReadCurve(clip, "CurveBlue") ?? ReadCurve(clip, "CurveB")
                ]),
            "TranslationClip" => CreatePropertyClip<CinematicPositionClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY"), ReadCurve(clip, "CurveZ")]),
            "RotationClip" => CreatePropertyClip<CinematicRotationClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY"), ReadCurve(clip, "CurveZ")]),
            "ScaleClip" => CreatePropertyClip<CinematicScaleClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY")]),
            "SecondaryTransformClip" => CreatePropertyClip<CinematicSecondaryTransformClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY")]),
            "ProportionClip" => CreatePropertyClip<CinematicProportionClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY")]),
            "Proportion3DClip" => CreatePropertyClip<CinematicProportion3DClipBinary>(
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveX"), ReadCurve(clip, "CurveY"), ReadCurve(clip, "CurveZ")]),
            "MaterialGraphicDiffuseAlphaClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicDiffuseAlphaClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveA") ?? ReadCurve(clip, "Curve")]),
            "MaterialGraphicDiffuseColorClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicDiffuseColorClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveR"), ReadCurve(clip, "CurveG"), ReadCurve(clip, "CurveB")]),
            "MaterialGraphicEnableLayerClip" => CreateMaterialGraphicLayerEnableClip(
                clip,
                startFrame,
                durationFrames,
                targets),
            "MaterialGraphicUVTranslationClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicUvTranslationClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [
                    ReadCurve(clip, "CurveU") ?? ReadCurve(clip, "CurveTranslationU"),
                    ReadCurve(clip, "CurveV") ?? ReadCurve(clip, "CurveTranslationV")
                ]),
            "MaterialGraphicUVScrollClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicUvScrollClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveScrollU"), ReadCurve(clip, "CurveScrollV")]),
            "MaterialGraphicUVRotationClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicUvRotationClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveAngle"), ReadCurve(clip, "CurvePivotX"), ReadCurve(clip, "CurvePivotY")]),
            "MaterialGraphicUVScaleClip" => CreateMaterialGraphicClip<CinematicMaterialGraphicUvScaleClipBinary>(
                clip,
                startFrame,
                durationFrames,
                targets,
                [ReadCurve(clip, "CurveScaleU"), ReadCurve(clip, "CurveScaleV"), ReadCurve(clip, "CurvePivotX"), ReadCurve(clip, "CurvePivotY")]),
            _ => SkipUnsupportedClip(logger, tapePath, className)
        };
    }

    private static TapeClip CreateTapeLauncherClip(
        JsonElement clip,
        int startFrame,
        int durationFrames,
        IReadOnlyList<ActorTargetPath> targets)
    {
        string? tapeLabel = ReadString(clip, "TapeLabel");
        string[] tapeLabels = ReadStringArray(clip, "TapeLabels");
        CinematicTapeLauncherClip launcher = new(
            ReadInt(clip, "Action", 0),
            ReadInt(clip, "TapeChoice", 0),
            tapeLabel,
            tapeLabels);
        return new TapeClip(
            LegacyBinarySerializer.GetTypeId<CinematicTapeLauncherClipBinary>(),
            startFrame,
            durationFrames,
            null,
            targets,
            [],
            TapeLauncher: launcher);
    }

    private static TapeClip CreatePropertyClip<TClip>(
        int startFrame,
        int durationFrames,
        IReadOnlyList<ActorTargetPath> targets,
        IReadOnlyList<CinematicCurve?> curves)
        where TClip : CinematicTapeClipBinary =>
        new(
            LegacyBinarySerializer.GetTypeId<TClip>(),
            startFrame,
            durationFrames,
            null,
            targets,
            curves);

    private static TapeClip CreateMaterialGraphicClip<TClip>(
        JsonElement clip,
        int startFrame,
        int durationFrames,
        IReadOnlyList<ActorTargetPath> targets,
        IReadOnlyList<CinematicCurve?> curves)
        where TClip : CinematicTapeClipBinary
    {
        uint typeId = LegacyBinarySerializer.GetTypeId<TClip>();
        int layerIndex = ReadInt(clip, "LayerIdx", 0);
        int uvModifierIndex = ReadInt(clip, "UVModifierIdx", 0);
        return new TapeClip(
            typeId,
            startFrame,
            durationFrames,
            null,
            targets,
            curves,
            MaterialGraphic: CinematicVisualStateBuilder.BuildMaterialGraphicClip(
                typeId,
                layerIndex,
                uvModifierIndex,
                curves));
    }

    private static TapeClip CreateMaterialGraphicLayerEnableClip(
        JsonElement clip,
        int startFrame,
        int durationFrames,
        IReadOnlyList<ActorTargetPath> targets)
    {
        uint typeId = LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicEnableLayerClipBinary>();
        int layerIndex = ReadInt(clip, "LayerIdx", 0);
        int uvModifierIndex = ReadInt(clip, "UVModifierIdx", 0);
        bool enabled = ReadInt(clip, "LayerEnabled", 0) != 0;
        CinematicLayerEnable layerEnable = new(layerIndex, enabled);
        CinematicMaterialGraphicClip materialGraphic = new(
            CinematicMaterialGraphicClipKind.EnableLayer,
            layerIndex,
            uvModifierIndex,
            enabled);
        return new TapeClip(
            typeId,
            startFrame,
            durationFrames,
            null,
            targets,
            [],
            layerEnable,
            materialGraphic);
    }

    private static TapeClip? SkipUnsupportedClip(
        ILogger logger,
        string tapePath,
        string className)
    {
        logger.LogDebug("Skipping unsupported JSON cinematic tape clip '{ClassName}' in '{TapePath}'.", className, tapePath);
        return null;
    }

    private static ActorTargetPath[] ReadActorPaths(JsonElement root)
    {
        if (!root.TryGetProperty("ActorPaths", out JsonElement pathsElement) ||
            pathsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. pathsElement
            .EnumerateArray()
            .Select(path => ParseTargetPath(path.GetString() ?? string.Empty))];
    }

    private static IReadOnlyList<ActorTargetPath> ReadTargets(
        JsonElement clip,
        IReadOnlyList<ActorTargetPath> actorPaths)
    {
        if (!clip.TryGetProperty("ActorIndices", out JsonElement indicesElement) ||
            indicesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<ActorTargetPath> targets = [];
        foreach (JsonElement indexElement in indicesElement.EnumerateArray())
        {
            if (!indexElement.TryGetInt32(out int index) ||
                index < 0 ||
                index >= actorPaths.Count)
            {
                continue;
            }

            targets.Add(actorPaths[index]);
        }

        return targets;
    }

    private static ActorTargetPath ParseTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ActorTargetPath([]);

        return new ActorTargetPath(
            [.. path
                .Replace('\\', '|')
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]);
    }

    private static CinematicCurve? ReadCurve(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement curveWrapper) ||
            curveWrapper.ValueKind != JsonValueKind.Object ||
            !curveWrapper.TryGetProperty("Curve", out JsonElement curve) ||
            curve.ValueKind != JsonValueKind.Object ||
            !curve.TryGetProperty("__class", out JsonElement classElement))
        {
            return null;
        }

        return classElement.GetString() switch
        {
            "BezierCurveFloatConstant" => ReadConstantCurve(curve),
            "BezierCurveFloatLinear" => ReadLinearCurve(curve),
            "BezierCurveFloatMulti" => ReadMultiCurve(curve),
            _ => null
        };
    }

    private static CinematicCurve? ReadConstantCurve(JsonElement curve)
    {
        if (!TryReadFloat(curve, "Value", out float value))
            return null;

        return CinematicVisualStateBuilder.CreateConstantCurve(value);
    }

    private static CinematicCurve? ReadLinearCurve(JsonElement curve)
    {
        if (!TryReadPoint(curve, "ValueLeft", out float leftTime, out float leftValue) ||
            !TryReadPoint(curve, "ValueRight", out float rightTime, out float rightValue))
        {
            return null;
        }

        TryReadPoint(curve, "NormalLeftOut", out float rightHandleTime, out float rightHandleValue);
        TryReadPoint(curve, "NormalRightIn", out float leftHandleTime, out float leftHandleValue);
        return new CinematicCurve(
        [
            new(
                leftTime,
                leftValue,
                leftTime,
                leftValue,
                rightHandleTime,
                rightHandleValue),
            new(
                rightTime,
                rightValue,
                leftHandleTime,
                leftHandleValue,
                rightTime,
                rightValue)
        ]);
    }

    private static CinematicCurve? ReadMultiCurve(JsonElement curve)
    {
        if (!curve.TryGetProperty("Keys", out JsonElement keysElement) ||
            keysElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<CinematicKeyframe> keyframes = [];
        foreach (JsonElement keyElement in keysElement.EnumerateArray())
        {
            if (!TryReadPoint(keyElement, "Value", out float time, out float value))
                continue;

            TryReadPoint(keyElement, "NormalIn", out float leftTime, out float leftValue);
            TryReadPoint(keyElement, "NormalOut", out float rightTime, out float rightValue);
            keyframes.Add(new CinematicKeyframe(
                time,
                value,
                leftTime,
                leftValue,
                rightTime,
                rightValue));
        }

        return keyframes.Count == 0
            ? null
            : new CinematicCurve([.. keyframes.OrderBy(keyframe => keyframe.Time)]);
    }

    private static bool TryReadPoint(
        JsonElement element,
        string propertyName,
        out float x,
        out float y)
    {
        x = 0;
        y = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement point) ||
            point.ValueKind != JsonValueKind.Array ||
            point.GetArrayLength() < 2)
        {
            return false;
        }

        x = point[0].GetSingle();
        y = point[1].GetSingle();
        return true;
    }

    private static bool TryReadFloat(
        JsonElement element,
        string propertyName,
        out float value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement valueElement) ||
            valueElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = valueElement.GetSingle();
        return true;
    }

    private static int ReadInt(
        JsonElement element,
        string propertyName,
        int defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return defaultValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
            return intValue;

        return defaultValue;
    }

    private static string ReadString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static string[] ReadStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. values
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)];
    }
}
