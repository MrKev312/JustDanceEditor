using Avalonia.Headless.XUnit;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Editor.Tests;

public sealed class ScoringAdjustmentToolViewModelTests
{
    [AvaloniaFact]
    public void DraftChanges_DoNotWriteMsmUntilSave()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            string movesFolder = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MovesFolder);
            Directory.CreateDirectory(movesFolder);
            string path = Path.Combine(movesFolder, "move_a.msm");
            File.WriteAllBytes(path, CreateHeaderOnlyClassifier());

            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            package.HandCoachMoves["move_a"] = new CoachMoveDefinition { Duration = 24 };
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.LoadSelectedMove("move_a", isFullBody: false);
            tool.LowThreshold = 1.2;

            MotionClassifierHeader unsaved = MotionClassifierHeaderEditor.ReadHeader(File.ReadAllBytes(path));
            Assert.Equal(1.0f, unsaved.LowThreshold);
            Assert.True(tool.IsDirty);

            tool.SaveCommand.Execute(null);

            MotionClassifierHeader saved = MotionClassifierHeaderEditor.ReadHeader(File.ReadAllBytes(path));
            Assert.Equal(1.2f, saved.LowThreshold);
            Assert.False(tool.IsDirty);
        }
        finally
        {
            tool.Dispose();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void DefaultAndCustomizationToggles_WriteHeaderValuesOnlyOnSave()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            string movesFolder = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MovesFolder);
            Directory.CreateDirectory(movesFolder);
            string path = Path.Combine(movesFolder, "move_a.msm");
            File.WriteAllBytes(path, CreateHeaderOnlyClassifier());

            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            package.HandCoachMoves["move_a"] = new CoachMoveDefinition { Duration = 24 };
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.LoadSelectedMove("move_a", isFullBody: false);
            tool.LowThresholdDefault = true;
            tool.DirectionImpactFactorDefault = false;
            tool.AutoCorrelationThresholdDefault = false;
            tool.IgnoreDirection = true;
            tool.IgnoreAutocorrelation = true;

            MotionClassifierHeader unsaved = MotionClassifierHeaderEditor.ReadHeader(File.ReadAllBytes(path));
            Assert.Equal(1.0f, unsaved.LowThreshold);
            Assert.Equal(2U, unsaved.CustomizationBitField);
            Assert.False(tool.IsLowThresholdCustom);
            Assert.False(tool.IsDirectionImpactFactorCustom);
            Assert.False(tool.IsAutoCorrelationThresholdCustom);
            Assert.True(tool.IsDirty);

            tool.SaveCommand.Execute(null);

            MotionClassifierHeader saved = MotionClassifierHeaderEditor.ReadHeader(File.ReadAllBytes(path));
            Assert.Equal(-1.0f, saved.LowThreshold);
            Assert.Equal(3U, saved.CustomizationBitField);
            Assert.False(tool.IsDirty);
        }
        finally
        {
            tool.Dispose();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AdjustmentCurve_MapsDistanceToMoveSpaceScoreImmediatelyFromThresholdDraft()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            string movesFolder = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MovesFolder);
            Directory.CreateDirectory(movesFolder);
            File.WriteAllBytes(Path.Combine(movesFolder, "move_a.msm"), CreateHeaderOnlyClassifier());

            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            package.HandCoachMoves["move_a"] = new CoachMoveDefinition { Duration = 24 };
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.LoadSelectedMove("move_a", isFullBody: false);

            ScoringAdjustmentCurve? initial = tool.AdjustmentCurve;
            Assert.NotNull(initial);
            Assert.Equal(3, initial.Points.Count);
            Assert.Equal(0.0f, initial.Points[0].StatisticalDistance, precision: 3);
            Assert.Equal(100.0f, initial.Points[0].PercentageScore, precision: 3);
            Assert.Equal(1.0f, initial.Points[1].StatisticalDistance, precision: 3);
            Assert.Equal(100.0f, initial.Points[1].PercentageScore, precision: 3);
            Assert.Equal(3.0f, initial.Points[2].StatisticalDistance, precision: 3);
            Assert.Equal(0.0f, initial.Points[2].PercentageScore, precision: 3);
            Assert.Equal(3.0f, initial.Points[^1].StatisticalDistance, precision: 3);
            Assert.Equal(0.0f, initial.Points[^1].PercentageScore, precision: 3);

            tool.LowThreshold = 1.2;

            ScoringAdjustmentCurve? adjusted = tool.AdjustmentCurve;
            Assert.NotNull(adjusted);
            Assert.Equal(3, adjusted.Points.Count);
            Assert.Equal(1.2f, adjusted.Points[1].StatisticalDistance, precision: 3);
            Assert.Equal(100.0f, adjusted.Points[1].PercentageScore, precision: 3);
            Assert.Equal(3.0f, adjusted.Points[2].StatisticalDistance, precision: 3);
            Assert.Equal(0.0f, adjusted.Points[2].PercentageScore, precision: 3);
        }
        finally
        {
            tool.Dispose();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AdjustmentCurve_UsesLoadedMsmThresholdsBeforeAnySliderChange()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            string movesFolder = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MovesFolder);
            Directory.CreateDirectory(movesFolder);
            File.WriteAllBytes(Path.Combine(movesFolder, "move_a.msm"), CreateHeaderOnlyClassifier(lowThreshold: 0.8f, highThreshold: 4.0f));

            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            package.HandCoachMoves["move_a"] = new CoachMoveDefinition { Duration = 24 };
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.LoadSelectedMove("move_a", isFullBody: false);

            ScoringAdjustmentCurve? curve = tool.AdjustmentCurve;
            Assert.NotNull(curve);
            Assert.Equal(3, curve.Points.Count);
            Assert.Equal(0.0f, curve.Points[0].StatisticalDistance, precision: 3);
            Assert.Equal(100.0f, curve.Points[0].PercentageScore, precision: 3);
            Assert.Equal(0.8f, curve.Points[1].StatisticalDistance, precision: 3);
            Assert.Equal(100.0f, curve.Points[1].PercentageScore, precision: 3);
            Assert.Equal(4.0f, curve.Points[2].StatisticalDistance, precision: 3);
            Assert.Equal(0.0f, curve.Points[2].PercentageScore, precision: 3);
            Assert.Equal(4.0f, curve.Points[^1].StatisticalDistance, precision: 3);
            Assert.Equal(0.0f, curve.Points[^1].PercentageScore, precision: 3);
        }
        finally
        {
            tool.Dispose();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ParameterInputs_AreClampedToSupportedRanges()
    {
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            tool.LowThreshold = 99.0;
            tool.HighThreshold = 0.0;
            tool.AutoCorrelationThreshold = 2.0;
            tool.DirectionImpactFactor = -4.0;

            Assert.Equal(1.4, tool.LowThreshold, precision: 3);
            Assert.Equal(1.5, tool.HighThreshold, precision: 3);
            Assert.Equal(1.3, tool.AutoCorrelationThreshold, precision: 3);
            Assert.Equal(0.0, tool.DirectionImpactFactor, precision: 3);
        }
        finally
        {
            tool.Dispose();
        }
    }

    [AvaloniaFact]
    public void GuidedSensitivityControls_UpdateRawDraftAndIgnoreFlags()
    {
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            tool.UseAdvancedScoringTuning = false;
            Assert.True(tool.ShowGuidedTuningPanel);
            Assert.False(tool.ShowAdvancedTuningPanel);

            tool.AutoCorrelationSensitivity = 0.0;

            Assert.True(tool.IgnoreAutocorrelation);
            Assert.False(tool.AutoCorrelationThresholdDefault);
            Assert.Equal(1.3, tool.AutoCorrelationThreshold, precision: 3);
            Assert.Equal("0%", tool.AutoCorrelationSensitivityText);

            tool.AutoCorrelationSensitivity = 1.0;

            Assert.False(tool.IgnoreAutocorrelation);
            Assert.Equal(0.5, tool.AutoCorrelationThreshold, precision: 3);
            Assert.Equal("100%", tool.AutoCorrelationSensitivityText);

            tool.DirectionSensitivity = 0.0;

            Assert.True(tool.IgnoreDirection);
            Assert.False(tool.DirectionImpactFactorDefault);
            Assert.Equal(0.0, tool.DirectionImpactFactor, precision: 3);
            Assert.Equal("0%", tool.DirectionSensitivityText);

            tool.DirectionSensitivity = 0.35;

            Assert.False(tool.IgnoreDirection);
            Assert.Equal(0.35, tool.DirectionImpactFactor, precision: 3);
            Assert.Equal("35%", tool.DirectionSensitivityText);

            tool.UseAdvancedScoringTuning = true;
            Assert.False(tool.ShowGuidedTuningPanel);
            Assert.True(tool.ShowAdvancedTuningPanel);
        }
        finally
        {
            tool.Dispose();
        }
    }

    [AvaloniaFact]
    public void FullBodySelection_DoesNotUseHandMsm()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        ScoringAdjustmentToolViewModel tool = new();
        try
        {
            string movesFolder = IntermediatePackageLayout.Resolve(root, IntermediatePackageLayout.Assets.MovesFolder);
            Directory.CreateDirectory(movesFolder);
            File.WriteAllBytes(Path.Combine(movesFolder, "shared_id.msm"), CreateHeaderOnlyClassifier());

            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            package.HandCoachMoves["shared_id"] = new CoachMoveDefinition { Duration = 24 };
            package.FullBodyCoachMoves["shared_id"] = new CoachMoveDefinition { Duration = 24, MoveType = CoachMoveType.FullBodyTracking };
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.LoadSelectedMove("shared_id", isFullBody: true);

            Assert.True(tool.IsGestureSelection);
            Assert.False(tool.IsMsmAvailable);
            Assert.Contains("gesture", tool.AssetKindText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            tool.Dispose();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreateHeaderOnlyClassifier(
        float lowThreshold = 1.0f,
        float highThreshold = 3.0f,
        float autoCorrelationThreshold = 1.0f,
        float directionImpactFactor = -1.0f)
    {
        byte[] result = new byte[240];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, sizeof(uint)), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, sizeof(uint)), 7);
        WriteFixedString(result, 8, "move_a");
        WriteFixedString(result, 72, "testmap");
        WriteFixedString(result, 136, "Acc_Dev_Dir_NP");
        WriteSingle(result, 200, 0.75f);
        WriteSingle(result, 204, lowThreshold);
        WriteSingle(result, 208, highThreshold);
        WriteSingle(result, 212, autoCorrelationThreshold);
        WriteSingle(result, 216, directionImpactFactor);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(220, sizeof(ulong)), 0x211C000000000000UL);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(228, sizeof(uint)), 2);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(232, sizeof(int)), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(236, sizeof(uint)), 0);
        return result;
    }

    private static void WriteFixedString(byte[] data, int offset, string value)
        => Encoding.UTF8.GetBytes(value, data.AsSpan(offset, 64));

    private static void WriteSingle(byte[] data, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), BitConverter.SingleToInt32Bits(value));
}
