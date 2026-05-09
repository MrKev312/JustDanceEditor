using JustDanceEditor.Conversion.Abstractions;

using System.Linq;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtConversionStrategyTests
{
    [Theory]
    [InlineData("wii-2020", ConversionSupportStatus.Experimental)]
    [InlineData("x360-2019", ConversionSupportStatus.Experimental)]
    [InlineData("durango-2022", ConversionSupportStatus.KnownPartial)]
    [InlineData("nx-2022", ConversionSupportStatus.Stable)]
    [InlineData("wiiu-2019", ConversionSupportStatus.Stable)]
    public void GetExportTargets_AssignsExpectedSupportStatus(string targetCode, ConversionSupportStatus expectedStatus)
    {
        UbiArtConversionStrategy strategy = new();

        ConversionTargetDefinition target = strategy.GetExportTargets().Single(target => target.TargetCode == targetCode);

        Assert.Equal(expectedStatus, target.SupportStatus);
    }
}
