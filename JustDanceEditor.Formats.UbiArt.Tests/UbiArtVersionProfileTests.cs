using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtVersionProfileTests
{
    [Fact]
    public void PictoComparer_Alphanumeric_For_2018_Sorts_pictos_as_expected()
    {
        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer())
        {
            EngineNumericVersion = 2018
        };

        var cmp = profile.PictoNameComparer;

        List<string> input = ["amor_sh", "amor_po", "amor1_po"];
        List<string> sorted = [.. input.OrderBy(s => s, cmp)];

        Assert.Equal(["amor_po", "amor_sh", "amor1_po"], sorted);
    }

    [Fact]
    public void PictoComparer_NumericOrdering_For_2019_Sorts_pictos_as_expected()
    {
        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer())
        {
            EngineNumericVersion = 2019
        };

        var cmp = profile.PictoNameComparer;

        List<string> input = ["amor_sh", "amor_po", "amor1_po"];
        List<string> sorted = [.. input.OrderBy(s => s, cmp)];

        Assert.Equal(["amor1_po", "amor_po", "amor_sh"], sorted);
    }
}