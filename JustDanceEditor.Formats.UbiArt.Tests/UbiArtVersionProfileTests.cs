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
        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        profile.EngineNumericVersion = 2018;

        var cmp = profile.PictoNameComparer;

        var input = new List<string> { "amor_sh", "amor_po", "amor1_po" };
        var sorted = input.OrderBy(s => s, cmp).ToList();

        Assert.Equal(new[] { "amor_po", "amor_sh", "amor1_po" }, sorted);
    }

    [Fact]
    public void PictoComparer_NumericOrdering_For_2019_Sorts_pictos_as_expected()
    {
        UbiArtVersionProfile profile = new(UbiArtContainerStyle.Uncooked, UbiArtEngineVersion.Modern, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        profile.EngineNumericVersion = 2019;

        var cmp = profile.PictoNameComparer;

        var input = new List<string> { "amor_sh", "amor_po", "amor1_po" };
        var sorted = input.OrderBy(s => s, cmp).ToList();

        Assert.Equal(new[] { "amor1_po", "amor_po", "amor_sh" }, sorted);
    }
}