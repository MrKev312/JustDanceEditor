using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Scoring;

using System.Collections.ObjectModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public sealed record ScoringProfileOption(
    MotionRecordingScoringProfile Profile,
    string DisplayName,
    string Tooltip);

public partial class SettingsViewModel : ObservableObject, IDialogResult<bool>
{
    private readonly EditorSettingsService _settings;

    public ObservableCollection<ScoringProfileOption> ScoringProfiles { get; } =
    [
        new(
            MotionRecordingScoringProfile.Raw,
            "Raw",
            "Uses the raw MoveSpace percentage with the editor's original feedback and score weighting."),
        new(
            MotionRecordingScoringProfile.JDNow,
            "JDNow",
            "Uses JDNow-style post-processing on top of the raw MoveSpace score."),
        new(
            MotionRecordingScoringProfile.UbiArt,
            "UbiArt",
            "Uses JD2018/UbiArt-style phone scoring, feedback thresholds, gold move handling, and MoveSpace defaults."),
        new(
            MotionRecordingScoringProfile.JDNext,
            "JDNext",
            "Uses JDNext/JD2026-style phone scoring, feedback thresholds, gold move handling, and MoveSpace defaults.")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedScoringProfileTooltip))]
    public partial ScoringProfileOption? SelectedScoringProfile { get; set; }

    public string SelectedScoringProfileTooltip => SelectedScoringProfile?.Tooltip ?? string.Empty;

    public bool Result { get; private set; }

    public SettingsViewModel(EditorSettingsService settings)
    {
        _settings = settings;
        SelectedScoringProfile = ScoringProfiles.FirstOrDefault(option => option.Profile == settings.ScoringProfile)
            ?? ScoringProfiles.First();
    }

    public void Accept()
    {
        if (SelectedScoringProfile != null)
            _settings.ScoringProfile = SelectedScoringProfile.Profile;

        _settings.Save();
        Result = true;
    }

    public void Cancel()
    {
        Result = false;
    }
}
