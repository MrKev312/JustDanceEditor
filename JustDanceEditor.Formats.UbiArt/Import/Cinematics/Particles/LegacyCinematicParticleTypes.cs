using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal readonly record struct ParticleVisual(Vector2 Size, RgbTint Tint, float Alpha);

internal readonly record struct SimulatedParticle(
    Vector2 Position,
    float PositionZ,
    Vector2 Size,
    float Angle,
    RgbTint Tint,
    float Alpha,
    int AtlasIndex);

internal readonly record struct ParticleSpawnState(
    Vector2 InitialPosition,
    Vector2 InitialVelocity,
    float InitialAngle,
    float AngularSpeed,
    double ParticleLifeTimeSeconds);

internal readonly record struct EmissionSample(
    double AgeSeconds,
    double EventTimeSeconds,
    double UpdateStartTimeSeconds,
    double FirstUpdateDeltaSeconds,
    int ParticleOrdinal,
    double ParticleLifeTimeSeconds);

internal readonly record struct ActiveFxEmitterPlayback(
    int EmitterIndex,
    double ElapsedSeconds,
    double? GenerationEndSeconds,
    FxPlaybackOptions Options);

internal readonly record struct FxPlaybackOptions(
    bool EmitFromBase,
    bool UseActorSpeed,
    bool UseActorOrientation,
    bool UseActorAlpha,
    uint UseBoneOrientation,
    float DescriptorAngleOffsetRadians,
    float DescriptorMinDelaySeconds,
    float DescriptorMaxDelaySeconds,
    double DescriptorDelaySeconds)
{
    public static FxPlaybackOptions PlainParticleGenerator { get; } = new(
        EmitFromBase: false,
        UseActorSpeed: false,
        UseActorOrientation: true,
        UseActorAlpha: true,
        UseBoneOrientation: 0,
        DescriptorAngleOffsetRadians: 0.0f,
        DescriptorMinDelaySeconds: 0.0f,
        DescriptorMaxDelaySeconds: 0.0f,
        DescriptorDelaySeconds: 0.0);

    public static FxPlaybackOptions FromControl(LegacyCinematicFxControlTemplate control) => new(
        control.EmitFromBase,
        control.UseActorSpeed,
        control.UseActorOrientation,
        control.UseActorAlpha,
        control.UseBoneOrientation,
        DescriptorAngleOffsetRadians: 0.0f,
        DescriptorMinDelaySeconds: 0.0f,
        DescriptorMaxDelaySeconds: 0.0f,
        DescriptorDelaySeconds: 0.0);
}

internal sealed class ParticleRandom(int seed)
{
    internal const int MaxLong = 2147483647;
    internal const int Multiplier = 16807;
    internal const int Quotient = MaxLong / Multiplier;
    internal const int Remainder = MaxLong % Multiplier;

    public int Seed { get; private set; } = Math.Max(1, seed);

    public float GetFloat(float min, float max)
    {
        if (Math.Abs(max - min) <= 0.000001f)
            return min;

        return min + ((max - min) * NextUnit());
    }

    public int GetInt(int min, int max)
    {
        if (max <= min)
            return min;

        return min + (NextInt() % (max - min + 1));
    }

    private float NextUnit() =>
        NextInt() * (1.0f / MaxLong);

    private int NextInt()
    {
        long next;
        if (Seed <= Quotient)
        {
            next = (long)Seed * Multiplier % MaxLong;
        }
        else
        {
            long high = Seed / Quotient;
            long low = Seed % Quotient;
            long test = (Multiplier * low) - (Remainder * high);
            next = test > 0 ? test : test + MaxLong;
        }

        Seed = (int)next;
        return Seed;
    }
}