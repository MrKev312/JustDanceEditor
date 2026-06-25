using JustDanceEditor.Formats.UbiArt.Import.Audio;

using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class UbiArtSoundSetTemplateResolverTests
{
    [Fact]
    public void ExtractAudioPathCandidates_CombinesSerializedFolderAndFile()
    {
        byte[] bytes =
        [
            0, 0, 0, 1,
            .. Encoding.ASCII.GetBytes("world/jd5/iwillsurvive/audio/amb/"),
            0, 0, 0,
            .. Encoding.ASCII.GetBytes("amb_iwillsurvive_intro.wav"),
            0x52, 0xA9, 0x85, 0x23
        ];

        string[] candidates = UbiArtSoundSetTemplateResolver.ExtractAudioPathCandidates(
            bytes,
            "world/jd5/iwillsurvive/audio/amb/set_amb_iwillsurvive_intro.tpl");

        Assert.Contains("world/jd5/iwillsurvive/audio/amb/amb_iwillsurvive_intro.wav", candidates);
    }
}