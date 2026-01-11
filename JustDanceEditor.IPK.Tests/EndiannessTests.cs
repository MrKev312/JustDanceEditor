using JustDanceEditor.IPK;

namespace JustDanceEditor.IPK.Tests;

public class EndiannessTests
{
    [Fact]
    public void WriteInt32BigEndian_WritesCorrectByteOrder()
    {
        // Arrange
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        // Act
        // Write integer 1. 
        // Little Endian (Standard PC): 01 00 00 00
        // Big Endian (IPK format):     00 00 00 01
        writer.WriteInt32BigEndian(1);

        // Assert
        byte[] result = ms.ToArray();
        Assert.Equal([0x00, 0x00, 0x00, 0x01], result);
    }

    [Fact]
    public void ReadInt32BigEndian_ReadsCorrectValue()
    {
        // Arrange
        byte[] bigEndianData = [0x00, 0x00, 0x00, 0x02];
        using MemoryStream ms = new(bigEndianData);
        using BinaryReader reader = new(ms);

        // Act
        int value = reader.ReadInt32BigEndian();

        // Assert
        Assert.Equal(2, value);
    }

    [Fact]
    public void RoundTrip_Int64_BigEndian()
    {
        // Arrange
        long original = 123456789012345;
        using MemoryStream ms = new();

        // Act
        using (BinaryWriter writer = new(ms, System.Text.Encoding.Default, true))
        {
            writer.WriteInt64BigEndian(original);
        }

        ms.Position = 0;
        long result;
        using (BinaryReader reader = new(ms))
        {
            result = reader.ReadInt64BigEndian();
        }

        // Assert
        Assert.Equal(original, result);
    }
}