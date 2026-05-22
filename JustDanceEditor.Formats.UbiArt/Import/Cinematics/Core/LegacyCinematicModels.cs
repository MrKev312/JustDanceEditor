using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Globalization;
using System.Numerics;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal sealed record LegacyCinematicScene(IReadOnlyList<LegacyCinematicActor> Actors);

internal sealed record LegacyCinematicActor(
    IReadOnlyList<string> Path,
    string Name,
    int SourceOffset,
    int SiblingOrder,
    uint TypeId,
    float RelativeZ,
    float ScaleX,
    float ScaleY,
    uint XFlipped,
    float Angle,
    float PositionX,
    float PositionY,
    string TemplatePath,
    string? SubScenePath,
    string? TexturePath,
    IReadOnlyList<string> TexturePaths,
    string? MaterialPath,
    string? MeshPath,
    uint? VisualComponentTypeId,
    int AtlasIndex,
    TextureAnchor Anchor,
    float CustomAnchorX,
    float CustomAnchorY,
    int ScenePriority,
    RgbTint BaseTint,
    float BaseAlpha,
    LegacyCinematicActorBind? ParentBind,
    LegacyCinematicSinusParameters Sinus = default,
    LegacyCinematicParticleTemplate? ParticleTemplate = null,
    LegacyCinematicFxTemplate? FxTemplate = null)
{
    public string Key { get; } = LegacyCinematicNames.NormalizeKey(Path);
    public IReadOnlyList<string> PathKeys { get; } =
        [.. Enumerable.Range(1, Path.Count).Select(length => LegacyCinematicNames.NormalizeKey(Path.Take(length)))];
}

internal sealed record LegacyCinematicActorBind(
    string ParentActorName,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float OffsetAngle,
    float LocalScaleX,
    float LocalScaleY,
    uint UseParentFlip,
    int ScaleInheritProp,
    uint UseParentAlpha,
    uint UseParentColor);

internal sealed record LegacyCinematicPickableFields(
    float RelativeZ,
    float ScaleX,
    float ScaleY,
    uint XFlipped,
    string Name,
    float PositionX,
    float PositionY,
    float Angle,
    string TemplatePath);

internal sealed record LegacyCinematicActorTrailer(int ComponentVersion, LegacyCinematicActorBind? ParentBind);

internal readonly record struct LegacyCinematicSinusParameters(
    float AmplitudeX,
    float AmplitudeY,
    float AmplitudeZ,
    float Speed,
    float AngleX,
    float AngleY)
{
    public bool HasAnimatedOffset =>
        Math.Abs(Speed) > 0.000001f &&
        (Math.Abs(AmplitudeX) > 0.000001f ||
            Math.Abs(AmplitudeY) > 0.000001f ||
            Math.Abs(AmplitudeZ) > 0.000001f);
}

internal sealed record TemplateVisualInfo(
    string TexturePath,
    IReadOnlyList<string> TexturePaths,
    string? MaterialPath,
    string? MeshPath,
    uint VisualComponentTypeId,
    int AtlasIndex,
    TextureAnchor Anchor,
    float CustomAnchorX,
    float CustomAnchorY,
    LegacyCinematicParticleTemplate? ParticleTemplate = null,
    LegacyCinematicFxTemplate? FxTemplate = null);

internal sealed record MaterializedCinematicImage(
    string ActorKey,
    string TexturePath,
    Image<Bgra32> Image,
    RenderGeometry Geometry,
    LegacyCinematicMaterial Material,
    IReadOnlyList<MaterializedCinematicTexture> Textures,
    LegacyCinematicAtlas? Atlas = null,
    IReadOnlyList<MaterializedCinematicFxEmitter> FxEmitters = null!) : IDisposable
{
    public MaterializedCinematicImage(string actorKey, string texturePath, Image<Bgra32> image)
        : this(actorKey, texturePath, image, RenderGeometry.NoAtlasQuad, LegacyCinematicMaterial.Empty)
    {
    }

    public MaterializedCinematicImage(string actorKey, string texturePath, Image<Bgra32> image, RenderGeometry geometry)
        : this(actorKey, texturePath, image, geometry, LegacyCinematicMaterial.Empty)
    {
    }

    public MaterializedCinematicImage(
        string actorKey,
        string texturePath,
        Image<Bgra32> image,
        RenderGeometry geometry,
        LegacyCinematicMaterial material)
        : this(
            actorKey,
            texturePath,
            image,
            geometry,
            material,
            [MaterializedCinematicTexture.Create(texturePath, image)])
    {
    }

    public int Width => Image.Width;
    public int Height => Image.Height;
    public IReadOnlyList<MaterializedCinematicFxEmitter> FxEmitters { get; init; } = FxEmitters ?? [];

    public void Dispose()
    {
        HashSet<Image<Bgra32>> disposed = [];
        foreach (MaterializedCinematicTexture texture in Textures)
        {
            if (disposed.Add(texture.Image))
                texture.Dispose();
        }

        foreach (MaterializedCinematicFxEmitter emitter in FxEmitters)
            emitter.Image.Dispose();
    }
}

internal sealed record LegacyCinematicFxTemplate(
    IReadOnlyList<LegacyCinematicFxEmitterTemplate> Emitters,
    IReadOnlyList<LegacyCinematicFxControlTemplate> Controls,
    IReadOnlyDictionary<uint, LegacyCinematicFxControlTemplate> ControlsByNameId,
    uint DefaultFxNameId,
    uint TriggerFxNameId);

internal sealed record LegacyCinematicFxEmitterTemplate(
    int Index,
    uint DescriptorNameId,
    float AngleOffsetRadians,
    float MinDelaySeconds,
    float MaxDelaySeconds,
    string? Name,
    string TexturePath,
    string? MaterialPath,
    LegacyCinematicParticleTemplate ParticleTemplate);

internal sealed record LegacyCinematicFxControlTemplate(
    uint NameId,
    bool StopOnEndAnim,
    bool PlayOnce,
    bool EmitFromBase,
    bool UseActorSpeed,
    bool UseActorOrientation,
    bool UseActorAlpha,
    uint UseBoneOrientation,
    IReadOnlyList<uint> ParticleNameIds);

internal sealed record MaterializedCinematicFxEmitter(
    LegacyCinematicFxEmitterTemplate Template,
    MaterializedCinematicImage Image);

internal sealed record MaterializedCinematicTexture(
    string TexturePath,
    Image<Bgra32> Image,
    Bgra32[] Pixels) : IDisposable
{
    public int Width => Image.Width;
    public int Height => Image.Height;

    public static MaterializedCinematicTexture Create(string texturePath, Image<Bgra32> image)
    {
        Bgra32[] pixels = new Bgra32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return new MaterializedCinematicTexture(texturePath, image, pixels);
    }

    public void Dispose() => Image.Dispose();
}

internal sealed record RenderGeometry(
    double WidthWorld,
    double HeightWorld,
    double CenterX,
    double CenterY,
    CinematicGeometrySource Source,
    IReadOnlyList<RenderVertex> Vertices,
    IReadOnlyList<int> Indices)
{
    private static readonly int[] QuadIndices = [0, 1, 2, 0, 2, 3];

    public RenderGeometry(
        double widthWorld,
        double heightWorld,
        double centerX,
        double centerY,
        CinematicGeometrySource source)
        : this(
            widthWorld,
            heightWorld,
            centerX,
            centerY,
            source,
            CreateQuadVertices(widthWorld, heightWorld, centerX, centerY),
            QuadIndices)
    {
    }

    public static RenderGeometry NoAtlasQuad { get; } = new(2.0, 2.0, 0.0, 0.0, CinematicGeometrySource.NoAtlasQuad);

    public bool HasSinusVertices { get; } =
        Vertices.Any(vertex =>
            Math.Abs(vertex.SinusWeight) > 0.000001 ||
            Math.Abs(vertex.SinusOffsetTime) > 0.000001);

    public static RenderGeometry FromRectangleAtlas(Vector2 uv0, Vector2 uv1, LegacyCinematicUvParameters? uvParameters = null) =>
        new(
            2.0,
            2.0,
            0.0,
            0.0,
            CinematicGeometrySource.RectangleAtlasQuad,
            CreateQuadVertices(2.0, 2.0, 0.0, 0.0, uv0, uv1, uvParameters),
            QuadIndices);

    public static RenderGeometry FromMesh(
        IReadOnlyList<RenderVertex> vertices,
        IReadOnlyList<int> indices,
        CinematicGeometrySource source)
    {
        if (vertices.Count == 0)
            return NoAtlasQuad;

        double minX = vertices.Min(vertex => vertex.X);
        double maxX = vertices.Max(vertex => vertex.X);
        double minY = vertices.Min(vertex => vertex.Y);
        double maxY = vertices.Max(vertex => vertex.Y);
        return new RenderGeometry(
            maxX - minX,
            maxY - minY,
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            source,
            vertices,
            indices);
    }

    private static RenderVertex[] CreateQuadVertices(
        double widthWorld,
        double heightWorld,
        double centerX,
        double centerY) =>
        CreateQuadVertices(widthWorld, heightWorld, centerX, centerY, Vector2.Zero, Vector2.One);

    private static RenderVertex[] CreateQuadVertices(
        double widthWorld,
        double heightWorld,
        double centerX,
        double centerY,
        Vector2 uv0,
        Vector2 uv1,
        LegacyCinematicUvParameters? uvParameters = null)
    {
        double halfWidth = widthWorld * 0.5;
        double halfHeight = heightWorld * 0.5;
        RenderVertex CreateVertex(int index, double x, double y, double u, double v)
        {
            LegacyCinematicUvParameter? parameter = uvParameters?.HasWeight == true && uvParameters.Parameters.Count > index
                ? uvParameters.Parameters[index]
                : null;

            return new RenderVertex(
                x + (parameter?.OffsetX ?? 0),
                y + (parameter?.OffsetY ?? 0),
                parameter?.Depth ?? 0,
                u,
                v,
                parameter?.Weight ?? 0,
                (parameter?.OffsetSinus ?? 0) * Math.PI);
        }

        return
        [
            CreateVertex(0, -halfWidth + centerX, halfHeight + centerY, uv0.X, uv0.Y),
            CreateVertex(1, halfWidth + centerX, halfHeight + centerY, uv1.X, uv0.Y),
            CreateVertex(2, halfWidth + centerX, -halfHeight + centerY, uv1.X, uv1.Y),
            CreateVertex(3, -halfWidth + centerX, -halfHeight + centerY, uv0.X, uv1.Y)
        ];
    }
}

internal enum TextureAnchor
{
    TopLeft = 0,
    MiddleCenter = 1,
    MiddleLeft = 2,
    MiddleRight = 3,
    TopCenter = 4,
    TopRight = 5,
    BottomCenter = 6,
    BottomLeft = 7,
    BottomRight = 8,
    Custom = 9
}

internal readonly record struct RenderVertex(
    double X,
    double Y,
    double Z,
    double U,
    double V,
    double SinusWeight = 0.0,
    double SinusOffsetTime = 0.0);

internal sealed record LegacyCinematicMaterial(
    int BlendMode,
    IReadOnlyList<LegacyCinematicMaterialLayer> Layers)
{
    public static LegacyCinematicMaterial Empty { get; } = new(
        BlendMode: 2,
        [LegacyCinematicMaterialLayer.Default]);

    public LegacyCinematicUvSampler CreateLayer0Sampler(double elapsedSeconds)
    {
        LegacyCinematicMaterialLayer layer = Layers.Count > 0
            ? Layers[0]
            : LegacyCinematicMaterialLayer.Default;
        return layer.CreateSampler(elapsedSeconds);
    }

    public IReadOnlyList<LegacyCinematicUvSampler> CreateSamplers(
        double elapsedSeconds,
        CinematicMaterialRuntimeOverrides? materialOverrides = null)
    {
        if (Layers.Count == 0)
            return [LegacyCinematicMaterialLayer.Default.CreateSampler(elapsedSeconds)];

        List<LegacyCinematicUvSampler> samplers = new(Layers.Count);
        for (int i = 0; i < Layers.Count; i++)
        {
            LegacyCinematicMaterialLayer layer = Layers[i];
            if (materialOverrides != null)
                layer = materialOverrides.Apply(layer, i);

            samplers.Add(layer.CreateSampler(elapsedSeconds));
        }

        return samplers;
    }
}

internal sealed record LegacyCinematicMaterialLayer(
    bool Enabled,
    TextureAddressMode AddressModeU,
    TextureAddressMode AddressModeV,
    IReadOnlyList<LegacyCinematicUvModifier> UvModifiers,
    int BlendMode = 2,
    LegacyCinematicTextureUsage TextureUsage = LegacyCinematicTextureUsage.EntireTexture,
    LegacyCinematicMaterialColor? DiffuseColor = null)
{
    public static LegacyCinematicMaterialLayer Default { get; } = new(
        true,
        TextureAddressMode.Wrap,
        TextureAddressMode.Wrap,
        [],
        BlendMode: 2,
        LegacyCinematicTextureUsage.EntireTexture,
        LegacyCinematicMaterialColor.White);

    public LegacyCinematicUvSampler CreateSampler(double elapsedSeconds) =>
        new(
            AddressModeU,
            AddressModeV,
            [.. UvModifiers.Select(modifier => modifier.CreateState(elapsedSeconds))],
            Enabled,
            BlendMode,
            TextureUsage,
            DiffuseColor ?? LegacyCinematicMaterialColor.White);
}

internal readonly record struct LegacyCinematicUvModifier(
    float TranslationU,
    float TranslationV,
    bool AnimTranslationU,
    bool AnimTranslationV,
    float Rotation,
    float RotationOffsetU,
    float RotationOffsetV,
    bool AnimRotation,
    float ScaleU,
    float ScaleV,
    float ScaleOffsetU,
    float ScaleOffsetV)
{
    public LegacyCinematicUvModifierState CreateState(double elapsedSeconds)
    {
        float time = (float)elapsedSeconds;
        float angle = AnimRotation ? Rotation * time : Rotation;
        float translationU = AnimTranslationU ? TranslationU * time : TranslationU;
        float translationV = AnimTranslationV ? TranslationV * time : TranslationV;
        return new(
            translationU,
            translationV,
            angle,
            RotationOffsetU,
            RotationOffsetV,
            ScaleU,
            ScaleV,
            ScaleOffsetU,
            ScaleOffsetV);
    }
}

internal readonly record struct LegacyCinematicUvModifierState(
    float TranslationU,
    float TranslationV,
    float Rotation,
    float RotationOffsetU,
    float RotationOffsetV,
    float ScaleU,
    float ScaleV,
    float ScaleOffsetU,
    float ScaleOffsetV);

internal sealed record LegacyCinematicUvSampler(
    TextureAddressMode AddressModeU,
    TextureAddressMode AddressModeV,
    IReadOnlyList<LegacyCinematicUvModifierState> Modifiers,
    bool Enabled = true,
    int BlendMode = 2,
    LegacyCinematicTextureUsage TextureUsage = LegacyCinematicTextureUsage.EntireTexture,
    LegacyCinematicMaterialColor DiffuseColor = default)
{
    public static LegacyCinematicUvSampler Default { get; } = new(
        TextureAddressMode.Wrap,
        TextureAddressMode.Wrap,
        [],
        true,
        2,
        LegacyCinematicTextureUsage.EntireTexture,
        LegacyCinematicMaterialColor.White);
}

internal enum TextureAddressMode
{
    Wrap = 0,
    Mirror = 1,
    Clamp = 2,
    Border = 3
}

internal enum LegacyCinematicTextureUsage
{
    NoTexture = 0,
    AlphaIsRed = 1,
    AlphaIsGreen = 2,
    AlphaIsBlue = 3,
    AlphaIsAlpha = 4,
    EntireTexture = 5,
    RgbOnly = 6,
    PleoStackedAlpha = 7
}

internal readonly record struct LegacyCinematicMaterialColor(double Red, double Green, double Blue, double Alpha)
{
    public static LegacyCinematicMaterialColor White { get; } = new(1, 1, 1, 1);

    public bool IsWhite =>
        Math.Abs(Red - 1) < 0.0001 &&
        Math.Abs(Green - 1) < 0.0001 &&
        Math.Abs(Blue - 1) < 0.0001 &&
        Math.Abs(Alpha - 1) < 0.0001;
}

internal sealed record CinematicMaterialRuntimeOverrides(
    IReadOnlyDictionary<int, bool> LayerEnabled,
    IReadOnlyDictionary<int, CinematicMaterialColorOverride> LayerColors,
    IReadOnlyDictionary<CinematicMaterialUvModifierKey, CinematicMaterialUvModifierOverride> UvModifiers)
{
    public bool IsEmpty =>
        LayerEnabled.Count == 0 &&
        LayerColors.Count == 0 &&
        UvModifiers.Count == 0;

    public string CacheKey
    {
        get
        {
            StringBuilder builder = new();
            foreach (KeyValuePair<int, bool> entry in LayerEnabled.OrderBy(entry => entry.Key))
            {
                builder.Append("|le:");
                builder.Append(entry.Key);
                builder.Append('=');
                builder.Append(entry.Value ? '1' : '0');
            }

            foreach (KeyValuePair<int, CinematicMaterialColorOverride> entry in LayerColors.OrderBy(entry => entry.Key))
            {
                builder.Append("|lc:");
                builder.Append(entry.Key);
                AppendNullable(builder, entry.Value.Red);
                AppendNullable(builder, entry.Value.Green);
                AppendNullable(builder, entry.Value.Blue);
                AppendNullable(builder, entry.Value.Alpha);
            }

            foreach (KeyValuePair<CinematicMaterialUvModifierKey, CinematicMaterialUvModifierOverride> entry in UvModifiers
                .OrderBy(entry => entry.Key.LayerIndex)
                .ThenBy(entry => entry.Key.ModifierIndex))
            {
                CinematicMaterialUvModifierOverride value = entry.Value;
                builder.Append("|uv:");
                builder.Append(entry.Key.LayerIndex);
                builder.Append(':');
                builder.Append(entry.Key.ModifierIndex);
                AppendNullable(builder, value.TranslationU);
                AppendNullable(builder, value.TranslationV);
                builder.Append(value.DisableAnimTranslation ? ":dt1" : ":dt0");
                AppendNullable(builder, value.Rotation);
                AppendNullable(builder, value.RotationOffsetU);
                AppendNullable(builder, value.RotationOffsetV);
                builder.Append(value.DisableAnimRotation ? ":dr1" : ":dr0");
                AppendNullable(builder, value.ScaleU);
                AppendNullable(builder, value.ScaleV);
                AppendNullable(builder, value.ScaleOffsetU);
                AppendNullable(builder, value.ScaleOffsetV);
            }

            return builder.ToString();
        }
    }

    public LegacyCinematicMaterialLayer Apply(LegacyCinematicMaterialLayer layer, int layerIndex)
    {
        bool enabled = LayerEnabled.TryGetValue(layerIndex, out bool enabledOverride)
            ? enabledOverride
            : layer.Enabled;
        LegacyCinematicMaterialColor? diffuseColor = layer.DiffuseColor;
        if (LayerColors.TryGetValue(layerIndex, out CinematicMaterialColorOverride colorOverride))
        {
            LegacyCinematicMaterialColor baseColor = diffuseColor ?? LegacyCinematicMaterialColor.White;
            diffuseColor = new LegacyCinematicMaterialColor(
                colorOverride.Red ?? baseColor.Red,
                colorOverride.Green ?? baseColor.Green,
                colorOverride.Blue ?? baseColor.Blue,
                colorOverride.Alpha ?? baseColor.Alpha);
        }

        IReadOnlyList<LegacyCinematicUvModifier> modifiers = ApplyUvOverrides(layer.UvModifiers, layerIndex);
        return layer with
        {
            Enabled = enabled,
            DiffuseColor = diffuseColor,
            UvModifiers = modifiers
        };
    }

    private IReadOnlyList<LegacyCinematicUvModifier> ApplyUvOverrides(
        IReadOnlyList<LegacyCinematicUvModifier> modifiers,
        int layerIndex)
    {
        if (UvModifiers.Count == 0 || modifiers.Count == 0)
            return modifiers;

        List<LegacyCinematicUvModifier>? mutable = null;
        foreach (KeyValuePair<CinematicMaterialUvModifierKey, CinematicMaterialUvModifierOverride> entry in UvModifiers)
        {
            if (entry.Key.LayerIndex != layerIndex ||
                entry.Key.ModifierIndex < 0 ||
                entry.Key.ModifierIndex >= modifiers.Count)
            {
                continue;
            }

            mutable ??= [.. modifiers];
            LegacyCinematicUvModifier modifier = mutable[entry.Key.ModifierIndex];
            CinematicMaterialUvModifierOverride value = entry.Value;
            mutable[entry.Key.ModifierIndex] = modifier with
            {
                TranslationU = value.TranslationU ?? modifier.TranslationU,
                TranslationV = value.TranslationV ?? modifier.TranslationV,
                AnimTranslationU = !value.DisableAnimTranslation && modifier.AnimTranslationU,
                AnimTranslationV = !value.DisableAnimTranslation && modifier.AnimTranslationV,
                Rotation = value.Rotation ?? modifier.Rotation,
                RotationOffsetU = value.RotationOffsetU ?? modifier.RotationOffsetU,
                RotationOffsetV = value.RotationOffsetV ?? modifier.RotationOffsetV,
                AnimRotation = !value.DisableAnimRotation && modifier.AnimRotation,
                ScaleU = value.ScaleU ?? modifier.ScaleU,
                ScaleV = value.ScaleV ?? modifier.ScaleV,
                ScaleOffsetU = value.ScaleOffsetU ?? modifier.ScaleOffsetU,
                ScaleOffsetV = value.ScaleOffsetV ?? modifier.ScaleOffsetV
            };
        }

        return mutable ?? modifiers;
    }

    private static void AppendNullable(StringBuilder builder, double? value)
    {
        builder.Append(':');
        builder.Append(value?.ToString("R", CultureInfo.InvariantCulture) ?? "_");
    }

    private static void AppendNullable(StringBuilder builder, float? value)
    {
        builder.Append(':');
        builder.Append(value?.ToString("R", CultureInfo.InvariantCulture) ?? "_");
    }
}

internal readonly record struct CinematicMaterialColorOverride(
    double? Red,
    double? Green,
    double? Blue,
    double? Alpha);

internal readonly record struct CinematicMaterialUvModifierKey(int LayerIndex, int ModifierIndex);

internal readonly record struct CinematicMaterialUvModifierOverride(
    float? TranslationU,
    float? TranslationV,
    bool DisableAnimTranslation,
    float? Rotation,
    float? RotationOffsetU,
    float? RotationOffsetV,
    bool DisableAnimRotation,
    float? ScaleU,
    float? ScaleV,
    float? ScaleOffsetU,
    float? ScaleOffsetV);