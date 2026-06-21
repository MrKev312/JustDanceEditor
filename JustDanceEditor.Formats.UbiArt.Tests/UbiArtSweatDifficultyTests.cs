using JustDanceEditor.Formats.UbiArt.Import.Intermediate;
using JustDanceEditor.Formats.UbiArt.Model;

using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtSweatDifficultyTests
{
    [Fact]
    public void EffectiveSweatDifficulty_UsesEnergy_WhenSweatDifficultyIsMissing()
    {
        SongDesc songDesc = JsonSerializer.Deserialize<SongDesc>("""
            {
              "COMPONENTS": [
                {
                  "MapName": "TexasHoldEm",
                  "Energy": 2
                }
              ]
            }
            """)!;

        Assert.Equal(2u, songDesc.Components[0].EffectiveSweatDifficulty);
    }

    [Fact]
    public void NormalizeSweatDifficulty_DefaultsToOne_WhenSweatDifficultyAndEnergyAreZero()
    {
        InfoComponent info = new()
        {
            MapName = "ZeroEnergy"
        };

        uint sweatDifficulty = IntermediatePackageBuilder.NormalizeSweatDifficulty(info, NullLogger.Instance);

        Assert.Equal(1u, sweatDifficulty);
    }
}
