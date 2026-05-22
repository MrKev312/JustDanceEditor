using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal sealed record SceneReadResult(IReadOnlyList<LegacyCinematicActor> Actors, int EndOffset);