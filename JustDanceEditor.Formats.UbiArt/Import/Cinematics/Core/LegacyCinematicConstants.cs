namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal static class LegacyCinematicConstants
{
    public const int OutputFramesPerSecond = 25;
    public const int TapeTicksPerSecond = 60;
    public const double TapeFramesPerBeat = 24.0;
    public const double MarkerSamplesPerSecond = 48000.0;
    public const int MaxTapeVisits = 512;

    public const int MaterialGraphicTailLength = 48;
    public const int MaterialGraphicAtlasIndexOffset = 80;
    public const int MaterialGraphicAnchorOffset = 80;
    public const int MaterialGraphicCustomAnchorXOffset = 84;
    public const int MaterialGraphicCustomAnchorYOffset = 88;
    public const int MaterialGraphicTailSinusAmplitudeXOffset = 0x14;
    public const int MaterialGraphicTailSinusAmplitudeYOffset = 0x18;
    public const int MaterialGraphicTailSinusAmplitudeZOffset = 0x1C;
    public const int MaterialGraphicTailSinusSpeedOffset = 0x20;
    public const int MaterialGraphicTailAngleXOffset = 0x24;
    public const int MaterialGraphicTailAngleYOffset = 0x28;
    public const int BindScaleInheritUseParent = 0;
    public const int BindScaleInheritUseChild = 1;
    public const int BindScaleInheritCombine = 2;

    public const float GraphHalfWidth = 4.0f;
    public const float GraphHalfHeight = 2.0f;
    public const float GraphPixelsPerWorldUnit = 128.0f;
    public const int GraphWidth = (int)(GraphHalfWidth * 2.0f * GraphPixelsPerWorldUnit);
    public const int GraphHeight = (int)(GraphHalfHeight * 2.0f * GraphPixelsPerWorldUnit);
}