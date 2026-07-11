using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class IntermediatePackageLayoutTests
{
    [Fact]
    public void PictogramFile_WithSimpleId_ReturnsPackageRelativePath()
    {
        Assert.Equal("assets/pictograms/picto_a.webp", IntermediatePackageLayout.Assets.PictogramFile("picto_a"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("..")]
    [InlineData("/escape")]
    [InlineData(@"C:\escape")]
    [InlineData("nested/picto")]
    [InlineData(@"nested\picto")]
    public void PictogramFile_WithUnsafeId_ThrowsArgumentException(string pictogramId)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            IntermediatePackageLayout.Assets.PictogramFile(pictogramId));

        Assert.Equal("pictogramId", exception.ParamName);
    }

    [Fact]
    public void GestureFolder_WithSimpleSubfolder_ReturnsPackageRelativePath()
    {
        Assert.Equal("assets/gestures/fullBody", IntermediatePackageLayout.Assets.GestureFolder("fullBody"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("..")]
    [InlineData("/escape")]
    [InlineData(@"C:\escape")]
    [InlineData("nested/gesture")]
    [InlineData(@"nested\gesture")]
    public void GestureFolder_WithUnsafeSubfolder_ThrowsArgumentException(string subfolderName)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            IntermediatePackageLayout.Assets.GestureFolder(subfolderName));

        Assert.Equal("subfolderName", exception.ParamName);
    }
}
