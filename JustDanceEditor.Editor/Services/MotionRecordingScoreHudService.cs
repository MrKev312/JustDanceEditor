using CommunityToolkit.Mvvm.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public partial class MotionRecordingScoreHudService : ObservableObject
{
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial int TotalScore { get; set; }

    [ObservableProperty]
    public partial string FeedbackText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MoveScoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double FeedbackOpacity { get; set; }

    public void Reset(bool clearTotal)
    {
        FeedbackText = string.Empty;
        MoveScoreText = string.Empty;
        FeedbackOpacity = 0.0;
        if (clearTotal)
            TotalScore = 0;
    }
}