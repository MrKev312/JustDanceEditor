using KevInc.UbiArt.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal sealed record SceneReadResult(IReadOnlyList<CinematicActor> Actors, int EndOffset);
