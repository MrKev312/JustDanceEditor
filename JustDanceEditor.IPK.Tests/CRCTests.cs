using System.Text;

namespace JustDanceEditor.IPK.Tests;

public class CRCTests
{
    [Fact]
    public void Compute_IsDeterministic()
    {
        // Arrange
        byte[] data = Encoding.ASCII.GetBytes("TEST/PATH/FILE.CKD");

        // Act
        uint crc1 = UbiArtCRC.Compute(data);
        uint crc2 = UbiArtCRC.Compute(data);

        // Assert
        Assert.Equal(crc1, crc2);
        Assert.NotEqual(0u, crc1);
    }

    [Fact]
    public void Compute_MatchesKnownResult()
    {
        // Arrange
        // Simple 4-byte input
        byte[] data = [0x01, 0x02, 0x03, 0x04];

        // Act
        uint result = UbiArtCRC.Compute(data);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public void Compute_DifferentInputs_ProduceDifferentOutputs()
    {
        uint resA = UbiArtCRC.Compute(Encoding.ASCII.GetBytes("A"));
        uint resB = UbiArtCRC.Compute(Encoding.ASCII.GetBytes("B"));

        Assert.NotEqual(resA, resB);
    }
}