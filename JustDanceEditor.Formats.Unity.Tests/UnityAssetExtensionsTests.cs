using System;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityAssetExtensionsTests
{
    [Fact]
    public void ToUnity_ConvertGuidToUintArray_ReturnsCorrectArray()
    {
        // Arrange
        Guid guid = new("12345678-1234-5678-1234-567812345678");

        // Act
        uint[] result = guid.ToUnity();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        Assert.All(result, x => Assert.IsType<uint>(x));
    }

    [Fact]
    public void ToUnity_WithEmptyGuid_ReturnsValidArray()
    {
        // Arrange
        Guid emptyGuid = Guid.Empty;

        // Act
        uint[] result = emptyGuid.ToUnity();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        Assert.Equal(0u, result[0]);
        Assert.Equal(0u, result[1]);
        Assert.Equal(0u, result[2]);
        Assert.Equal(0u, result[3]);
    }

    [Fact]
    public void ToUnity_WithDifferentGuids_ProducesDifferentArrays()
    {
        // Arrange
        Guid guid1 = Guid.NewGuid();
        Guid guid2 = Guid.NewGuid();

        // Act
        uint[] result1 = guid1.ToUnity();
        uint[] result2 = guid2.ToUnity();

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void ToUnity_IsConsistent_SameGuidProducesSameResult()
    {
        // Arrange
        Guid guid = new("12345678-1234-5678-1234-567812345678");

        // Act
        uint[] result1 = guid.ToUnity();
        uint[] result2 = guid.ToUnity();

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void ToUnity_WithMaxValueGuid_ReturnsValidArray()
    {
        // Arrange
        Guid guid = new(uint.MaxValue, ushort.MaxValue, ushort.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        // Act
        uint[] result = guid.ToUnity();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
    }
}