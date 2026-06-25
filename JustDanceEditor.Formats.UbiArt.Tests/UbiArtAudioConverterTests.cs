using JustDanceEditor.Formats.UbiArt.Import.Audio;

using System.IO;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtAudioConverterTests
{
    [Fact]
    public void IsOggOpusStream_ReturnsTrue_ForOggOpus()
    {
        using MemoryStream stream = new(CreateOggHeader("OpusHead"));

        Assert.True(UbiArtAudioConverter.IsOggOpusStream(stream));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void IsOggOpusStream_ReturnsFalse_ForOggVorbis()
    {
        using MemoryStream stream = new(CreateOggHeader("vorbis"));

        Assert.False(UbiArtAudioConverter.IsOggOpusStream(stream));
        Assert.Equal(0, stream.Position);
    }

    private static byte[] CreateOggHeader(string codecMarker)
    {
        byte[] data = new byte[64];
        Encoding.ASCII.GetBytes("OggS").CopyTo(data, 0);
        Encoding.ASCII.GetBytes(codecMarker).CopyTo(data, 28);
        return data;
    }
}