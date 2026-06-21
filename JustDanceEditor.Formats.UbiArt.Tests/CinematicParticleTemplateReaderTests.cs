using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Particles;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicParticleTemplateReaderTests
{
    [Fact]
    public void ParticleTemplateReader_ReadsSourceSerializedParameterBlock()
    {
        byte[] bytes = CreateParticleTemplateBytes();

        bool read = CinematicParticleTemplateReader.TryRead(bytes, out CinematicParticleTemplate template);

        Assert.True(read);
        Assert.Equal(0x40, template.Parameters.SourceOffset);
        Assert.Equal((uint)5, template.Parameters.MaxParticles);
        Assert.Equal(uint.MaxValue, template.Parameters.EmitParticlesCount);
        Assert.True(template.Parameters.RenderInReflection);
        AssertClose(-90, template.Parameters.VelocityAngle);
        AssertClose(-3, template.Parameters.Gravity.Y);
        AssertClose(0.05, template.Parameters.Frequency);
        Assert.Equal((uint)2, template.Parameters.PhaseCount);
        AssertClose(-1, template.Parameters.GenerationBox.Min.X);
        AssertClose(20, template.Parameters.BoundingBox.Max.Y);
        AssertClose(0.7, template.Parameters.UniformScale);
        Assert.True(template.Parameters.UseActorTranslation);
        Assert.Equal(2, template.Phases.Count);
        AssertClose(0.3, template.Phases[0].PhaseTime);
        AssertClose(0, template.Phases[0].ColorMin.Alpha);
        Assert.Equal(35, template.Phases[0].AnimEnd);
        AssertClose(2.75, template.Phases[1].PhaseTime);
        Assert.Equal(24, template.Phases[1].AnimEnd);
    }


    [Fact]
    public void ParticleTemplateReader_RejectsNonParticleMaterialBytes()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(TextureAddressMode.Wrap, TextureAddressMode.Wrap, modifier);

        bool read = CinematicParticleTemplateReader.TryRead(bytes, out _);

        Assert.False(read);
    }
}
