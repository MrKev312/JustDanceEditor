using JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtPictoConverterTests
{
    [Fact]
    public void DeduplicatePictoSources_UsesGeneratedOutputName()
    {
        CookedFile[] sources =
        [
            new("world/maps/360/timeline/pictos/clap1_cl.png.ckd"),
            new("cache/itf_cooked/nx/world/maps/360/timeline/pictos/clap1_cl.png.ckd"),
            new("world/maps/360/timeline/pictos/turn2_ar.png.ckd")
        ];

        CookedFile[] deduped = UbiArtPictoConverter.DeduplicatePictoSources(sources, NullLogger.Instance);

        Assert.Equal(2, deduped.Length);
        Assert.Equal("world/maps/360/timeline/pictos/clap1_cl.png.ckd", deduped[0].RelativePath);
        Assert.Equal("world/maps/360/timeline/pictos/turn2_ar.png.ckd", deduped[1].RelativePath);
    }

    [Fact]
    public void DeduplicatePictoSources_IgnoresOutputNameCase()
    {
        CookedFile[] sources =
        [
            new("world/maps/360/timeline/pictos/Turn2_Ar.png.ckd"),
            new("cache/itf_cooked/nx/world/maps/360/timeline/pictos/turn2_ar.png.ckd")
        ];

        CookedFile[] deduped = UbiArtPictoConverter.DeduplicatePictoSources(sources, NullLogger.Instance);

        Assert.Single(deduped);
        Assert.Equal("world/maps/360/timeline/pictos/Turn2_Ar.png.ckd", deduped[0].RelativePath);
    }
}
