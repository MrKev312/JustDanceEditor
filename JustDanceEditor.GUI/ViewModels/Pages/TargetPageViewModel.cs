using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.AppHost;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.GUI.Services;
using JustDanceEditor.GUI.ViewModels.Prompts;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels.Pages;

public sealed partial class TargetPageViewModel : ViewModelBase
{
    private const string DefaultPlatformCode = "pc";
    private const string DefaultTargetCode = "jdi";

    private readonly IApplicationDialogService _dialogs;
    private readonly ConversionTargetDefinition[] _targets;
    private bool _updatingCoverGenerators;
    private bool _mapBackgroundSourceUserSelected;
    private bool _albumCoachSourceUserSelected;
    private string? _previewSourceFormatName;
    private string? _previewMaterializedRoot;
    private bool _downloadAssetsWhenLoading = true;

    public TargetPageViewModel(
        IEnumerable<IFormatConversionStrategy> strategies,
        IApplicationDialogService dialogs)
    {
        _dialogs = dialogs;
        _targets = ConversionTargetSelector.GetAvailableTargets(strategies);
        LoadTargets();
    }

    public ObservableCollection<PlatformItemViewModel> Platforms { get; } = [];

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<PromptInputViewModel> Prompts { get; } = [];

    public ObservableCollection<CoverGeneratorItemViewModel> SquareCoverGenerators { get; } = [];

    public ObservableCollection<CoverGeneratorItemViewModel> WideCoverGenerators { get; } = [];

    public ObservableCollection<CoverAssetSourceItemViewModel> MapBackgroundSources { get; } = [];

    public ObservableCollection<CoverAssetSourceItemViewModel> AlbumCoachSources { get; } = [];

    public IReadOnlyList<ConversionTargetDefinition> AvailableTargets => _targets;

    public ConversionTargetDefinition? CurrentTarget => SelectedTarget?.Target;

    public bool CanConvert => SelectedTarget is not null;

    [ObservableProperty]
    public partial PlatformItemViewModel? SelectedPlatform { get; set; }

    [ObservableProperty]
    public partial TargetItemViewModel? SelectedTarget { get; set; }

    [ObservableProperty]
    public partial CoverGeneratorItemViewModel? SelectedSquareCoverGenerator { get; set; }

    [ObservableProperty]
    public partial CoverGeneratorItemViewModel? SelectedWideCoverGenerator { get; set; }

    [ObservableProperty]
    public partial CoverAssetSourceItemViewModel? SelectedMapBackgroundSource { get; set; }

    [ObservableProperty]
    public partial CoverAssetSourceItemViewModel? SelectedAlbumCoachSource { get; set; }

    [ObservableProperty]
    public partial bool IsSquareCoverGeneratorVisible { get; set; }

    [ObservableProperty]
    public partial bool IsWideCoverGeneratorVisible { get; set; }

    [ObservableProperty]
    public partial bool AreCoverGeneratorOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsCompositionSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsMapBackgroundSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAlbumCoachSourceOptionsVisible { get; set; }

    [ObservableProperty]
    public partial string TargetSupportText { get; set; } = string.Empty;

    public event EventHandler? SelectedTargetUpdated;

    public event EventHandler? CoverOptionsUpdated;

    partial void OnSelectedPlatformChanged(PlatformItemViewModel? value)
    {
        LoadTargetsForPlatform(value);
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentTarget));
        OnPropertyChanged(nameof(CanConvert));
        TargetSupportText = value is null ? string.Empty : GetSupportText(value.Target);
        RebuildPrompts(value?.Target.ExportPrompts ?? [], skipOutputPath: true);
        RebuildCoverGeneratorOptions();
        SelectedTargetUpdated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedSquareCoverGeneratorChanged(CoverGeneratorItemViewModel? value)
    {
        UpdateCompositionSourceOptionsVisibility();
        NotifyCoverOptionsUpdated();
    }

    partial void OnSelectedWideCoverGeneratorChanged(CoverGeneratorItemViewModel? value)
    {
        UpdateCompositionSourceOptionsVisibility();
        NotifyCoverOptionsUpdated();
    }

    partial void OnSelectedMapBackgroundSourceChanged(CoverAssetSourceItemViewModel? value)
    {
        if (!_updatingCoverGenerators && value is not null)
            _mapBackgroundSourceUserSelected = true;

        NotifyCoverOptionsUpdated();
    }

    partial void OnSelectedAlbumCoachSourceChanged(CoverAssetSourceItemViewModel? value)
    {
        if (!_updatingCoverGenerators && value is not null)
            _albumCoachSourceUserSelected = true;

        NotifyCoverOptionsUpdated();
    }

    public void SelectTarget(ConversionTargetDefinition target)
    {
        PlatformItemViewModel? platform = Platforms.FirstOrDefault(item =>
            item.Platform.PlatformCode.Equals(target.Platform.PlatformCode, StringComparison.OrdinalIgnoreCase));
        if (platform is null)
            return;

        if (!ReferenceEquals(SelectedPlatform, platform))
            SelectedPlatform = platform;

        TargetItemViewModel? targetItem = Targets.FirstOrDefault(item =>
            item.Target.TargetCode.Equals(target.TargetCode, StringComparison.OrdinalIgnoreCase) &&
            item.Target.FormatCode.Equals(target.FormatCode, StringComparison.OrdinalIgnoreCase));
        if (targetItem is not null)
            SelectedTarget = targetItem;
    }

    public void UpdatePreviewContext(
        string? sourceFormatName,
        string? materializedRoot,
        bool downloadAssetsWhenLoading)
    {
        _previewSourceFormatName = sourceFormatName;
        _previewMaterializedRoot = materializedRoot;
        _downloadAssetsWhenLoading = downloadAssetsWhenLoading;
        RebuildCoverGeneratorOptions();
    }

    public void ResetCoverSourceSelections()
    {
        _mapBackgroundSourceUserSelected = false;
        _albumCoachSourceUserSelected = false;
    }

    internal CoverPreviewOptions CreateCoverPreviewOptions() =>
        new(
            _downloadAssetsWhenLoading,
            ResolveSquareCoverGeneratorKind(),
            ResolveWideCoverGeneratorKind(),
            SelectedMapBackgroundSource?.Kind,
            SelectedAlbumCoachSource?.Kind);

    public CoverGeneratorKind ResolveSquareCoverGeneratorKind() =>
        SelectedSquareCoverGenerator?.Kind ?? CoverGeneratorKind.Automatic;

    public CoverGeneratorKind ResolveWideCoverGeneratorKind() =>
        SelectedWideCoverGenerator?.Kind ?? CoverGeneratorKind.Automatic;

    public PromptAnswerSet BuildConversionAnswers(string outputPath, string? songName)
    {
        PromptAnswerSet answers = new();
        answers.Set(ConversionPromptIds.OutputPath, outputPath);

        foreach (KeyValuePair<string, string> answer in PromptInputAnswerBuilder.Build(Prompts).Answers)
            answers.Set(answer.Key, answer.Value);

        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        return answers;
    }

    private void LoadTargets()
    {
        Platforms.Clear();

        foreach (PlatformItemViewModel platform in ConversionTargetSelector.GetSortedPlatforms(_targets)
            .Select(platform => new PlatformItemViewModel(platform)))
        {
            Platforms.Add(platform);
        }

        SelectedPlatform = Platforms.FirstOrDefault(platform =>
                platform.Platform.PlatformCode.Equals(DefaultPlatformCode, StringComparison.OrdinalIgnoreCase))
            ?? Platforms.FirstOrDefault();
    }

    private void LoadTargetsForPlatform(PlatformItemViewModel? platform)
    {
        Targets.Clear();
        if (platform is null)
        {
            SelectedTarget = null;
            return;
        }

        foreach (TargetItemViewModel target in ConversionTargetSelector
            .GetSortedTargetsForPlatform(_targets, platform.Platform.PlatformCode)
            .Select(target => new TargetItemViewModel(target)))
        {
            Targets.Add(target);
        }

        SelectedTarget = Targets.FirstOrDefault(target =>
                target.Target.TargetCode.Equals(DefaultTargetCode, StringComparison.OrdinalIgnoreCase)
                || target.Target.FormatCode.Equals(DefaultTargetCode, StringComparison.OrdinalIgnoreCase))
            ?? Targets.FirstOrDefault();
    }

    private void RebuildCoverGeneratorOptions()
    {
        _updatingCoverGenerators = true;
        try
        {
            CoverGeneratorOptionResult options = CoverGeneratorOptionService.Build(new CoverGeneratorOptionRequest(
                Target: CurrentTarget,
                SourceFormatName: _previewSourceFormatName,
                Inventory: new CoverAssetInventory(_previewMaterializedRoot, _downloadAssetsWhenLoading),
                PreviousSquareGenerator: SelectedSquareCoverGenerator?.Kind,
                PreviousWideGenerator: SelectedWideCoverGenerator?.Kind,
                PreviousMapBackgroundSource: SelectedMapBackgroundSource?.Kind,
                PreviousAlbumCoachSource: SelectedAlbumCoachSource?.Kind,
                PreserveMapBackgroundSource: _mapBackgroundSourceUserSelected,
                PreserveAlbumCoachSource: _albumCoachSourceUserSelected));

            ReplaceCollection(SquareCoverGenerators, options.SquareGenerators);
            ReplaceCollection(WideCoverGenerators, options.WideGenerators);
            ReplaceCollection(MapBackgroundSources, options.MapBackgroundSources);
            ReplaceCollection(AlbumCoachSources, options.AlbumCoachSources);

            SelectedSquareCoverGenerator = options.SelectedSquareGenerator;
            SelectedWideCoverGenerator = options.SelectedWideGenerator;
            SelectedMapBackgroundSource = options.SelectedMapBackgroundSource;
            SelectedAlbumCoachSource = options.SelectedAlbumCoachSource;
            IsSquareCoverGeneratorVisible = options.IsSquareCoverGeneratorVisible;
            IsWideCoverGeneratorVisible = options.IsWideCoverGeneratorVisible;
            AreCoverGeneratorOptionsVisible = options.AreCoverGeneratorOptionsVisible;
            UpdateCompositionSourceOptionsVisibility();
        }
        finally
        {
            _updatingCoverGenerators = false;
        }
    }

    private void UpdateCompositionSourceOptionsVisibility()
    {
        bool compositionGeneratorSelected =
            AreCoverGeneratorOptionsVisible &&
            ((SelectedSquareCoverGenerator?.Kind == CoverGeneratorKind.FromMapBackground) ||
             (SelectedWideCoverGenerator?.Kind == CoverGeneratorKind.FromMapBackground));

        IsMapBackgroundSourceOptionsVisible = compositionGeneratorSelected && MapBackgroundSources.Count > 1;
        IsAlbumCoachSourceOptionsVisible = compositionGeneratorSelected && AlbumCoachSources.Count > 1;
        IsCompositionSourceOptionsVisible = IsMapBackgroundSourceOptionsVisible || IsAlbumCoachSourceOptionsVisible;
    }

    private void RebuildPrompts(
        IReadOnlyList<ConversionPrompt> prompts,
        bool skipOutputPath)
    {
        Prompts.Clear();
        foreach (ConversionPrompt prompt in prompts)
        {
            if (skipOutputPath && prompt.Id.Equals(ConversionPromptIds.OutputPath, StringComparison.OrdinalIgnoreCase))
                continue;

            Prompts.Add(PromptInputViewModel.Create(prompt, _dialogs));
        }
    }

    private void NotifyCoverOptionsUpdated()
    {
        if (!_updatingCoverGenerators)
            CoverOptionsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
            target.Add(item);
    }

    private static string GetSupportText(ConversionTargetDefinition target) => target.SupportStatus switch
    {
        ConversionSupportStatus.Experimental => "Experimental target. Import/export may need extra verification.",
        ConversionSupportStatus.KnownPartial => "Known partial support. Basic conversion works, but platform-native dumps are still limited.",
        _ => string.Empty
    };
}