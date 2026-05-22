namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal readonly record struct MaterialLayerColor(double Red, double Green, double Blue, double Alpha);

internal readonly record struct RenderedRawFrame(int Frame, byte[] Buffer, int Length);