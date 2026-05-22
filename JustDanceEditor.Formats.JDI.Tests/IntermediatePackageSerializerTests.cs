using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class IntermediatePackageSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesVibrationTimeline()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            IntermediateSongPackage package = new()
            {
                Metadata = new IntermediateMetadata
                {
                    MapName = "test",
                    Title = "Test",
                    Artist = "Tests",
                    OriginalJDVersion = 2018,
                    CoachCount = 1
                },
                Vibrations = new Timeline<VibrationClip>
                {
                    Clips =
                    [
                        new VibrationClip
                        {
                            Id = 123,
                            TrackId = 456,
                            StartTime = 32,
                            Duration = 24,
                            VibrationFilePath = "world/_common/hd_rumble/bigpulse_01.vib",
                            PlayerId = -1,
                            Modulation = 0.5f
                        },
                        new VibrationClip
                        {
                            Id = 124,
                            TrackId = 789,
                            StartTime = 64,
                            Duration = 48,
                            VibrationFilePath = "world/_common/hd_rumble/special.vib",
                            Loop = 1,
                            DeviceSide = 2,
                            PlayerId = 3,
                            Context = 4,
                            StartTimeOffset = 5,
                            Modulation = 0.75f
                        }
                    ]
                }
            };

            IntermediatePackageSerializer.WriteToFolder(package, root);
            string vibrationsJson = File.ReadAllText(IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Timelines.VibrationsFile));
            Assert.DoesNotContain("isActive", vibrationsJson);
            Assert.DoesNotContain("\"playerId\": -1", vibrationsJson);
            Assert.DoesNotContain("\"modulation\": 0.5", vibrationsJson);

            IntermediateSongPackage loaded = IntermediatePackageSerializer.LoadFromFolder(root);

            Assert.Equal(2, loaded.Vibrations.Clips.Count);
            VibrationClip clip = loaded.Vibrations.Clips[0];
            Assert.Equal(123, clip.Id);
            Assert.Equal(456, clip.TrackId);
            Assert.Equal(32, clip.StartTime);
            Assert.Equal(24, clip.Duration);
            Assert.Equal("world/_common/hd_rumble/bigpulse_01.vib", clip.VibrationFilePath);
            Assert.Null(clip.PlayerId);
            Assert.Null(clip.Modulation);

            VibrationClip custom = loaded.Vibrations.Clips[1];
            Assert.Equal(1, custom.Loop);
            Assert.Equal(2, custom.DeviceSide);
            Assert.Equal(3, custom.PlayerId);
            Assert.Equal(4, custom.Context);
            Assert.Equal(5, custom.StartTimeOffset);
            Assert.Equal(0.75f, custom.Modulation);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}