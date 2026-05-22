using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

using SixLabors.ImageSharp;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal sealed record LegacyCinematicAtlas(
    double Width,
    double Height,
    IReadOnlyDictionary<int, LegacyCinematicUvData> UvMap,
    IReadOnlyDictionary<int, LegacyCinematicUvParameters> UvParameters);

internal sealed record LegacyCinematicUvData(IReadOnlyList<Vector2> Uvs);

internal readonly record struct LegacyCinematicUvRect(float Left, float Top, float Right, float Bottom)
{
    public static LegacyCinematicUvRect Full { get; } = new(0, 0, 1, 1);
}

internal sealed record LegacyCinematicUvParameters(
    float Orientation,
    string Flag,
    IReadOnlyList<LegacyCinematicUvParameter> Parameters,
    IReadOnlyList<LegacyCinematicUvTriangle> Triangles,
    bool HasWeight,
    bool HasColor);

internal sealed record LegacyCinematicUvParameter(
    float Weight,
    float Depth,
    uint Color,
    float OffsetX,
    float OffsetY,
    float OffsetSinus);

internal readonly record struct LegacyCinematicUvTriangle(int Index0, int Index1, int Index2);

internal sealed record RenderableCinematicActor(
    LegacyCinematicActor Actor,
    MaterializedCinematicImage? Image,
    RenderGeometry Geometry,
    CinematicLayerPlane Plane,
    int ScenePriority,
    float DrawOrderZ,
    int PrimitiveTieBreak,
    CinematicRenderKind RenderKind = CinematicRenderKind.Image);

internal sealed record FrameDrawItem(
    RenderableCinematicActor Actor,
    ResolvedActorState State,
    ProjectedQuad Quad,
    CinematicMaterialRuntimeOverrides? MaterialOverrides = null,
    LegacyCinematicUvRect? UvOverride = null,
    int ParticleIndex = -1,
    int ParticleAtlasIndex = -1,
    int ParticleEmitterIndex = -1,
    MaterializedCinematicImage? ImageOverride = null,
    RenderGeometry? GeometryOverride = null,
    LegacyCinematicParticleTemplate? ParticleTemplateOverride = null,
    double MaterialElapsedSeconds = double.NaN,
    float? SortDepthOverride = null,
    LegacyCinematicCamera? CameraOverride = null);

internal readonly record struct ProjectedQuad(
    Vector2 TopLeft,
    Vector2 TopRight,
    Vector2 BottomRight,
    Vector2 BottomLeft,
    Rectangle Bounds)
{
    public static ProjectedQuad Empty { get; } = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Rectangle.Empty);
}

internal readonly record struct LegacyCinematicCamera(
    double PositionX,
    double PositionY,
    double PositionZ,
    double FovYRadians,
    double AspectRatio)
{
    public const double DefaultFovYRadians = Math.PI * 25.0 / 180.0;

    public static LegacyCinematicCamera CreateDefault(int outputWidth, int outputHeight) =>
        new(
            0,
            0,
            10,
            DefaultFovYRadians,
            outputHeight <= 0 ? 1.0 : outputWidth / (double)outputHeight);
}

internal sealed record ResolvedActorState(
    float PositionX,
    float PositionY,
    float PositionZ,
    float ScaleX,
    float ScaleY,
    float Angle,
    float Alpha,
    RgbTint Tint,
    bool XFlipped,
    float RotationX = 0.0f,
    float RotationY = 0.0f);

internal sealed record LocalActorState(
    float PositionX,
    float PositionY,
    float PositionZ,
    float ScaleX,
    float ScaleY,
    float Angle,
    float Alpha,
    RgbTint Tint,
    bool XFlipped,
    float RotationX = 0.0f,
    float RotationY = 0.0f);

internal sealed record LegacyCinematicTapeData(
    IReadOnlyList<PropertyClip> PropertyClips,
    IReadOnlyList<SourceEvaluationClip> SourceEvaluationClips,
    int ParsedTapeCount,
    int RenderStartFrame,
    int MaterialTimeStartFrame);

internal sealed record TapeVisit(string Path, int TimeOffsetFrames);

internal enum LegacyCinematicTapeReferenceLoopingType
{
    Off = 0,
    Loop = 1,
    PingPong = 2,
    Reverse = 3,
    Infinite = 4,
    PingPongInfinite = 5
}

internal sealed record TapeClip(
    uint TypeId,
    int SerializedSize,
    int StartFrame,
    int DurationFrames,
    string? Path,
    IReadOnlyList<ActorTargetPath> Targets,
    IReadOnlyList<CinematicCurve?> Curves,
    CinematicLayerEnable? LayerEnable = null,
    CinematicMaterialGraphicClip? MaterialGraphic = null,
    uint FxNameId = 0,
    bool KillParticlesOnEnd = false,
    LegacyCinematicTapeReferenceLoopingType LoopingType = LegacyCinematicTapeReferenceLoopingType.Off);

internal sealed record ActorTargetPath(IReadOnlyList<string> Segments)
{
    public string Key { get; } = LegacyCinematicNames.NormalizeKey(Segments);
}

internal sealed record PropertyClip(
    ActorTargetPath Target,
    uint TypeId,
    int StartFrame,
    int DurationFrames,
    CinematicVisualState State,
    int Order,
    uint FxNameId = 0,
    bool KillParticlesOnEnd = false,
    bool IsResolvedFromAncestor = false,
    int ResolvedActorCount = 0);

internal sealed record SourceEvaluationClip(
    int? PropertyOrder,
    uint TypeId,
    int StartFrame,
    int DurationFrames,
    int ResolvedActorCount = 0);

internal sealed record ActiveFxClip(uint NameId, int StartFrame, int DurationFrames, bool IsInClipRange, bool KillParticlesOnEnd);

internal sealed record ActiveFxPlayback(uint NameId, double ElapsedSeconds, double? GenerationEndSeconds);

internal sealed record CinematicVisualState(
    CinematicTransform? Transform,
    CinematicMaterial? Material,
    CinematicLayerEnable? LayerEnable = null,
    CinematicMaterialGraphicClip? MaterialGraphic = null);

internal sealed record CinematicLayerEnable(int LayerIndex, bool Enabled);

internal sealed record CinematicMaterialGraphicClip(
    CinematicMaterialGraphicClipKind Kind,
    int LayerIndex,
    int UvModifierIndex,
    bool Enabled = true,
    CinematicCurve? Red = null,
    CinematicCurve? Green = null,
    CinematicCurve? Blue = null,
    CinematicCurve? Alpha = null,
    CinematicCurve? U = null,
    CinematicCurve? V = null,
    CinematicCurve? Angle = null,
    CinematicCurve? PivotX = null,
    CinematicCurve? PivotY = null,
    CinematicCurve? ScaleU = null,
    CinematicCurve? ScaleV = null);

internal enum CinematicMaterialGraphicClipKind
{
    EnableLayer,
    DiffuseAlpha,
    DiffuseColor,
    UvTranslation,
    UvRotation,
    UvScale
}

internal sealed record CinematicTransform(
    CinematicCurve? PositionX,
    CinematicCurve? PositionY,
    CinematicCurve? PositionZ,
    CinematicCurve? Rotation,
    CinematicCurve? ScaleX,
    CinematicCurve? ScaleY,
    CinematicScaleMode ScaleMode = CinematicScaleMode.Absolute,
    CinematicCurve? RotationX = null,
    CinematicCurve? RotationY = null);

internal sealed record CinematicMaterial(CinematicCurve? Red, CinematicCurve? Green, CinematicCurve? Blue, CinematicCurve? Alpha)
{
    public bool HasColor => Red != null || Green != null || Blue != null;
}

internal readonly record struct CinematicKeyframe(
    float Time,
    float Value,
    float LeftHandleTime,
    float LeftHandleValue,
    float RightHandleTime,
    float RightHandleValue);

internal sealed record CinematicCurve(IReadOnlyList<CinematicKeyframe> Keyframes);

internal enum CinematicScaleMode
{
    Absolute,
    Proportional
}