using JustDanceEditor.Formats.UbiArt.Import.Audio;
using JustDanceEditor.Formats.UbiArt.Model;

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

    [Fact]
    public void AudioStartOffset_UsesMarkerForPositiveStartBeat_NotVideoStartTime()
    {
        JDUbiArtSong song = CreateSong(
            startBeat: 2,
            videoStartTime: -9f,
            markers: [0, 27168, 54336]);

        Assert.Equal(-1.132f, song.GetAudioStartOffset(), precision: 6);
    }

    [Fact]
    public void AudioStartOffset_ExtrapolatesNegativeStartBeat_NotVideoStartTimeOrAbsoluteMarker()
    {
        JDUbiArtSong song = CreateSong(
            startBeat: -9,
            videoStartTime: -0.967f,
            markers: [0, 24000, 48000, 72000, 96000, 120000, 144000, 168000, 192000, 210911]);

        Assert.Equal(4.5f, song.GetAudioStartOffset(), precision: 6);
    }

    private static byte[] CreateOggHeader(string codecMarker)
    {
        byte[] data = new byte[64];
        Encoding.ASCII.GetBytes("OggS").CopyTo(data, 0);
        Encoding.ASCII.GetBytes(codecMarker).CopyTo(data, 28);
        return data;
    }

    private static JDUbiArtSong CreateSong(int startBeat, float videoStartTime, int[] markers) => new()
    {
        MusicTrack = new MusicTrack
        {
            Components =
            [
                new TrackDataHolder
                {
                    TrackData = new TrackData
                    {
                        Structure = new Structure
                        {
                            StartBeat = startBeat,
                            VideoStartTime = videoStartTime,
                            Markers = markers
                        }
                    }
                }
            ]
        }
    };
}