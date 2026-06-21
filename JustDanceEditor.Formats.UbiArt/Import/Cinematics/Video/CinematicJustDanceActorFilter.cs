using KevInc.UbiArt.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class CinematicJustDanceActorFilter
{
    public static bool ShouldRenderActor(CinematicActor actor) =>
        !IsNonCinematicJustDanceSceneActor(actor);

    private static bool IsNonCinematicJustDanceSceneActor(CinematicActor actor)
    {
        if (actor.Path.Count > 0 && IsNonCinematicJustDanceRoot(actor.Path[0]))
            return true;

        if (actor.TexturePath is { } texturePath)
        {
            string normalizedTexturePath = CinematicNames.NormalizePath(texturePath);
            if (normalizedTexturePath.Contains("/timeline/pictos/", StringComparison.OrdinalIgnoreCase) ||
                normalizedTexturePath.Contains("/menuart/", StringComparison.OrdinalIgnoreCase) ||
                normalizedTexturePath.Contains("/autodance/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonCinematicJustDanceRoot(string rootName) =>
        rootName.EndsWith("_TML", StringComparison.OrdinalIgnoreCase) ||
        rootName.EndsWith("_TIMELINE", StringComparison.OrdinalIgnoreCase) ||
        rootName.EndsWith("_MENUART", StringComparison.OrdinalIgnoreCase) ||
        rootName.EndsWith("_AUDIO", StringComparison.OrdinalIgnoreCase) ||
        rootName.EndsWith("_AUTODANCE", StringComparison.OrdinalIgnoreCase);
}
