using JustDanceEditor.IPK;

namespace JustDanceEditor.IPK.Tests;

public class CompressionTests
{
    [Fact]
    public void Compress_And_Decompress_RoundTrip_ReturnsOriginalData()
    {
        // Arrange
        byte[] originalData = new byte[1024];
        new Random().NextBytes(originalData); // Fill with random noise

        // Act
        byte[] compressed = Compressor.Compress(originalData);
        byte[] decompressed = Decompressor.Decompress(compressed);

        // Assert
        Assert.NotEqual(originalData.Length, compressed.Length); // Ideally compressed is smaller, but with random noise it might be larger/same, just distinct
        Assert.Equal(originalData, decompressed);
    }

    [Fact]
    public void Compressor_Adds_ZlibHeader()
    {
        // Arrange
        byte[] data = "Hello World"u8.ToArray();

        // Act
        byte[] compressed = Compressor.Compress(data);

        // Assert
        // Zlib header check: 0x78 is the standard CMF byte for Deflate (32K window)
        Assert.Equal(0x78, compressed[0]);
    }

    [Fact]
    public void Decompressor_Handles_RawDeflate_Or_Zlib()
    {
        // Test compatibility logic in Decompressor.cs
        // 0x78 is explicitly checked in the source code to skip bytes.

        byte[] rawData = "Test Data"u8.ToArray();
        byte[] compressedWithHeader = Compressor.Compress(rawData);

        // Decompress valid Zlib
        byte[] result1 = Decompressor.Decompress(compressedWithHeader);
        Assert.Equal(rawData, result1);
    }
}