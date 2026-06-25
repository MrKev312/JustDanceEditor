using JustDanceEditor.Conversion.Abstractions;

using System.Linq;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtConversionStrategyTests
{
    [Theory]
    [InlineData("wii-2020", ConversionSupportStatus.Stable)]
    [InlineData("ps3-2014", ConversionSupportStatus.Stable)]
    [InlineData("ps3-2015", ConversionSupportStatus.Stable)]
    [InlineData("ps3-2016", ConversionSupportStatus.Stable)]
    [InlineData("ps3-2017", ConversionSupportStatus.Stable)]
    [InlineData("ps3-2018", ConversionSupportStatus.Stable)]
    [InlineData("x360-2014", ConversionSupportStatus.Stable)]
    [InlineData("x360-2015", ConversionSupportStatus.Stable)]
    [InlineData("x360-2016", ConversionSupportStatus.Stable)]
    [InlineData("x360-2017", ConversionSupportStatus.Stable)]
    [InlineData("x360-2018", ConversionSupportStatus.Stable)]
    [InlineData("x360-2019", ConversionSupportStatus.Stable)]
    [InlineData("durango-2014", ConversionSupportStatus.KnownPartial)]
    [InlineData("durango-2022", ConversionSupportStatus.KnownPartial)]
    [InlineData("ps4-2022", ConversionSupportStatus.Stable)]
    [InlineData("nx-2022", ConversionSupportStatus.Stable)]
    [InlineData("wiiu-2019", ConversionSupportStatus.Stable)]
    public void GetExportTargets_AssignsExpectedSupportStatus(string targetCode, ConversionSupportStatus expectedStatus)
    {
        UbiArtConversionStrategy strategy = new();

        ConversionTargetDefinition target = strategy.GetExportTargets().Single(target => target.TargetCode == targetCode);

        Assert.Equal(expectedStatus, target.SupportStatus);
    }
}