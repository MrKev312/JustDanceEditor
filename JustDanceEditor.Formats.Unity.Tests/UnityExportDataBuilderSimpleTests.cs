using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging.Abstractions;

using System.Collections.Generic;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityExportDataBuilderSimpleTests
{
    [Fact]
    public void Create_WithCompleteMetadata_ReturnsUnityExportData()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            Title = "Test Song",
            Artist = "Test Artist",
            MapName = "TestMap",
            Difficulty = 5,
            CoachCount = 2
        };

        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestMap", result.Name);
        Assert.Equal("Test Song", result.Metadata.Title);
        Assert.Equal("Test Artist", result.Metadata.Artist);
        Assert.Equal(5u, result.Metadata.Difficulty);
    }

    [Fact]
    public void Create_WithoutMapName_UsesTitle()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            Title = "My Song Title"
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal("My Song Title", result.Name);
    }

    [Fact]
    public void Create_WithoutMapNameOrTitle_UsesDefaultName()
    {
        // Arrange
        IntermediateSongPackage package = new() { Metadata = new() };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal("Song", result.Name);
    }

    [Fact]
    public void Create_WithNullLyricsColor_UsesDefaultColor()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            LyricsColor = null!
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal("#FFFFFFFF", result.Metadata.LyricsColor);
    }

    [Fact]
    public void Create_WithCustomLyricsColor_PreservesColor()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            LyricsColor = "#FF0000FF"
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal("#FF0000FF", result.Metadata.LyricsColor);
    }

    [Fact]
    public void Create_MapsOriginalJDVersion_2014()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            OriginalJDVersion = 123
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal((uint)2014, result.Metadata.OriginalJDVersion);
    }

    [Fact]
    public void Create_MapsOriginalJDVersion_2017()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            OriginalJDVersion = 4884
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal((uint)2017, result.Metadata.OriginalJDVersion);
    }

    [Fact]
    public void Create_WithUnmappedVersion_PreservesOriginal()
    {
        // Arrange
        IntermediateMetadata metadata = new()
        {
            OriginalJDVersion = 9999
        };
        IntermediateSongPackage package = new() { Metadata = metadata };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal((uint)9999, result.Metadata.OriginalJDVersion);
    }

    [Fact]
    public void Create_WithLyricsClips_OrdersClipsByStartTime()
    {
        // Arrange
        List<KaraokeClip> lyrics =
        [
            new KaraokeClip { StartTime = 100 },
            new KaraokeClip { StartTime = 50 },
            new KaraokeClip { StartTime = 200 }
        ];

        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            Lyrics = new() { Clips = lyrics }
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal(3, result.KaraokeClips.Count);
        Assert.Equal(50, result.KaraokeClips[0].StartTime);
        Assert.Equal(100, result.KaraokeClips[1].StartTime);
        Assert.Equal(200, result.KaraokeClips[2].StartTime);
    }

    [Fact]
    public void Create_WithoutLyricsClips_EmptyCollection()
    {
        // Arrange
        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            Lyrics = null
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Empty(result.KaraokeClips);
    }

    [Fact]
    public void Create_WithPictogramClips_OrdersClipsByStartTime()
    {
        // Arrange
        List<PictogramClip> pictograms =
        [
            new PictogramClip { StartTime = 100 },
            new PictogramClip { StartTime = 50 },
            new PictogramClip { StartTime = 200 }
        ];

        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            Pictograms = new() { Clips = pictograms }
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal(3, result.PictogramClips.Count);
        Assert.Equal(50, result.PictogramClips[0].StartTime);
        Assert.Equal(100, result.PictogramClips[1].StartTime);
        Assert.Equal(200, result.PictogramClips[2].StartTime);
    }

    [Fact]
    public void Create_WithGoldEffectClips_OrdersClipsByStartTime()
    {
        // Arrange
        List<GoldEffectClip> goldEffects =
        [
            new GoldEffectClip { StartTime = 100 },
            new GoldEffectClip { StartTime = 50 },
            new GoldEffectClip { StartTime = 200 }
        ];

        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            GoldEffects = new() { Clips = goldEffects }
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal(3, result.GoldEffectClips.Count);
        Assert.Equal(50, result.GoldEffectClips[0].StartTime);
        Assert.Equal(100, result.GoldEffectClips[1].StartTime);
        Assert.Equal(200, result.GoldEffectClips[2].StartTime);
    }

    [Fact]
    public void Create_WithHideHudClips_OrdersClipsByStartTime()
    {
        // Arrange
        List<HideUserInterfaceClip> hideHudClips =
        [
            new HideUserInterfaceClip { StartTime = 100 },
            new HideUserInterfaceClip { StartTime = 50 },
            new HideUserInterfaceClip { StartTime = 200 }
        ];

        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            HideUserInterface = new() { Clips = hideHudClips }
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Equal(3, result.HideHudClips.Count);
        Assert.Equal(50, result.HideHudClips[0].StartTime);
        Assert.Equal(100, result.HideHudClips[1].StartTime);
        Assert.Equal(200, result.HideHudClips[2].StartTime);
    }

    [Fact]
    public void Create_WithoutCoaches_EmptyMotionClips()
    {
        // Arrange
        IntermediateSongPackage package = new()
        {
            Metadata = new IntermediateMetadata(),
            CoachTimelines = null,
            FullBodyCoachTimelines = null
        };

        // Act
        UnityExportData result = UnityExportDataBuilder.Create(package, NullLogger.Instance);

        // Assert
        Assert.Empty(result.MotionClips);
    }
}