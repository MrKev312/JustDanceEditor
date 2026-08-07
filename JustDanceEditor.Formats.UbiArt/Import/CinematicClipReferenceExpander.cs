using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace JustDanceEditor.Formats.UbiArt.Import;

internal sealed class CinematicClipReferenceExpander(ILogger logger)
{
    private static readonly HashSet<string> RenderOnlyClipClasses = new(StringComparer.Ordinal)
    {
        "ActorEnableClip", "AlphaClip", "ColorClip", "MaterialGraphicDiffuseAlphaClip",
        "MaterialGraphicDiffuseColorClip", "MaterialGraphicEnableLayerClip", "MaterialGraphicUVRotationClip",
        "MaterialGraphicUVScaleClip", "MaterialGraphicUVScrollClip", "MaterialGraphicUVTranslationClip",
        "Proportion3DClip", "ProportionClip", "RotationClip", "ScaleClip", "SecondaryTransformClip", "TranslationClip"
    };

    public IEnumerable<Clip> Expand(
        IEnumerable<Clip> clips,
        JustDanceUbiArtFileSystem fileSystem,
        JsonSerializerOptions options) =>
        Expand(clips, fileSystem, options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

    public static bool IsRenderOnlyClipClass(string? className) =>
        !string.IsNullOrWhiteSpace(className) && RenderOnlyClipClasses.Contains(className);

    private IEnumerable<Clip> Expand(
        IEnumerable<Clip> clips,
        JustDanceUbiArtFileSystem fileSystem,
        JsonSerializerOptions options,
        HashSet<string> recursionGuard,
        int timeOffset)
    {
        foreach (Clip clip in clips)
        {
            if (clip is UnknownClip unknown)
            {
                LogUnknownClip(unknown);
                continue;
            }

            if (clip is TapeReferenceClip reference)
            {
                foreach (Clip nested in LoadReference(reference, fileSystem, options, recursionGuard, timeOffset))
                    yield return nested;
                continue;
            }

            if (timeOffset != 0)
                clip.StartTime += timeOffset;
            yield return clip;
        }
    }

    private void LogUnknownClip(UnknownClip unknown)
    {
        if (IsRenderOnlyClipClass(unknown.OriginalClass))
        {
            logger.LogDebug(
                "Ignoring render-only cinematic clip type {ClipClass} at start {StartTime}, duration {Duration}.",
                unknown.OriginalClass, unknown.StartTime, unknown.Duration);
            return;
        }

        if (!string.IsNullOrWhiteSpace(unknown.OriginalClass))
        {
            logger.LogWarning(
                "Skipping unknown clip type {ClipClass} at start {StartTime}, duration {Duration}.",
                unknown.OriginalClass, unknown.StartTime, unknown.Duration);
            return;
        }

        logger.LogWarning(
            "Skipping unknown legacy clip type 0x{ClipTypeId:X8} at start {StartTime}, duration {Duration}.",
            unknown.TypeId, unknown.StartTime, unknown.Duration);
    }

    private IEnumerable<Clip> LoadReference(
        TapeReferenceClip reference,
        JustDanceUbiArtFileSystem fileSystem,
        JsonSerializerOptions options,
        HashSet<string> recursionGuard,
        int parentOffset)
    {
        if (string.IsNullOrWhiteSpace(reference.Path))
            yield break;

        string normalizedPath = reference.Path.Replace('\\', '/');
        if (!recursionGuard.Add(normalizedPath))
        {
            logger.LogWarning("Detected recursive tape reference '{Path}', skipping to avoid infinite loop.", reference.Path);
            yield break;
        }

        try
        {
            if (!fileSystem.GetFilePath(reference.Path, out CookedFile? tapePath))
            {
                logger.LogWarning("Referenced tape '{Path}' was not found.", reference.Path);
                yield break;
            }

            using Stream tapeStream = fileSystem.GetFileStream(tapePath);
            ClipTape tape = fileSystem.VersionProfile.Serializer != null
                ? SongDataLoader.DeserializeClipTape(fileSystem, tapeStream, options)
                : throw new InvalidOperationException("Serializer not configured on FileSystem.");
            foreach (Clip clip in Expand(tape.Clips, fileSystem, options, recursionGuard, parentOffset + reference.StartTime))
                yield return clip;
        }
        finally
        {
            recursionGuard.Remove(normalizedPath);
        }
    }
}
