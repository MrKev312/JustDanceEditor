using JustDanceEditor.Formats.JDI;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityConversionRequestTests
{
    [Fact]
    public void UnityConversionRequest_CanBeInstantiatedWithPaths()
    {
        // Arrange
        string inputPath = "/input/path";
        string outputPath = "/output/path";

        // Act
        UnityConversionRequest request = new(inputPath, outputPath);

        // Assert
        Assert.Equal(inputPath, request.InputPath);
        Assert.Equal(outputPath, request.OutputPath);
    }

    [Fact]
    public void UnityConversionRequest_DefaultsExportTypeToCustomServer()
    {
        // Act
        UnityConversionRequest request = new("/in", "/out");

        // Assert
        Assert.Equal(ExportType.CustomServer, request.ExportType);
    }

    [Fact]
    public void UnityConversionRequest_CanSetExportType()
    {
        // Arrange
        UnityConversionRequest request = new("/in", "/out")
        {
            // Act
            ExportType = ExportType.OfflineCache
        };

        // Assert
        Assert.Equal(ExportType.OfflineCache, request.ExportType);
    }

    [Fact]
    public void UnityConversionRequest_CanSetCacheNumber()
    {
        // Arrange
        UnityConversionRequest request = new("/in", "/out");
        uint cacheNumber = 123;

        // Act
        request.CacheNumber = cacheNumber;

        // Assert
        Assert.Equal(cacheNumber, request.CacheNumber);
    }

    [Fact]
    public void UnityConversionRequest_CacheNumberDefaultsToNull()
    {
        // Act
        UnityConversionRequest request = new("/in", "/out");

        // Assert
        Assert.Null(request.CacheNumber);
    }

    [Fact]
    public void UnityConversionRequest_CanChangeInputAndOutputPaths()
    {
        // Arrange
        UnityConversionRequest request = new("/in1", "/out1")
        {
            // Act
            InputPath = "/in2",
            OutputPath = "/out2"
        };

        // Assert
        Assert.Equal("/in2", request.InputPath);
        Assert.Equal("/out2", request.OutputPath);
    }
}

public class ConversionRequestBaseTests
{
    [Fact]
    public void ConversionRequestBase_CanBeInstantiatedWithPaths()
    {
        // Arrange
        string inputPath = "/input/path";
        string outputPath = "/output/path";

        // Act
        UnityConversionRequest request = new(inputPath, outputPath);

        // Assert
        Assert.Equal(inputPath, request.InputPath);
        Assert.Equal(outputPath, request.OutputPath);
    }

    [Fact]
    public void ConversionRequestBase_CanModifyPaths()
    {
        // Arrange
        ConversionRequestBase request = new UnityConversionRequest("/in1", "/out1")
        {
            // Act
            InputPath = "/in2",
            OutputPath = "/out2"
        };

        // Assert
        Assert.Equal("/in2", request.InputPath);
        Assert.Equal("/out2", request.OutputPath);
    }
}

public class ExportTypeTests
{
    [Fact]
    public void ExportType_OfflineCache_HasCorrectValue()
    {
        // Act & Assert
        Assert.Equal(0, (int)ExportType.OfflineCache);
    }

    [Fact]
    public void ExportType_CustomServer_HasCorrectValue()
    {
        // Act & Assert
        Assert.Equal(1, (int)ExportType.CustomServer);
    }

    [Fact]
    public void ExportType_CanBeParsedFromInt()
    {
        // Act
        ExportType offlineCache = 0;
        ExportType customServer = (ExportType)1;

        // Assert
        Assert.Equal(ExportType.OfflineCache, offlineCache);
        Assert.Equal(ExportType.CustomServer, customServer);
    }
}