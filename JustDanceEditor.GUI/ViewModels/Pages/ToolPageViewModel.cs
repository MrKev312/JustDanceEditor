using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.GUI.Services;
using JustDanceEditor.GUI.ViewModels.Prompts;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels.Pages;

public sealed partial class ToolPageViewModel(IApplicationDialogService dialogs) : ViewModelBase
{
    public ObservableCollection<PromptInputViewModel> Prompts { get; } = [];

    public ToolMenuItemViewModel? Selection { get; private set; }

    public bool HasSelection => Selection is not null;

    [ObservableProperty]
    public partial string TitleText { get; set; } = "Tool";

    [ObservableProperty]
    public partial string DescriptionText { get; set; } = string.Empty;

    public void Load(ToolMenuItemViewModel selection)
    {
        Selection = selection;
        TitleText = $"{selection.Provider.ProviderName} - {selection.Tool.DisplayName}";
        DescriptionText = selection.Tool.Description;
        RebuildPrompts(selection.Tool.Prompts);
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(HasSelection));
    }

    public PromptAnswerSet BuildAnswers() => PromptInputAnswerBuilder.Build(Prompts);

    private void RebuildPrompts(IReadOnlyList<ConversionPrompt> prompts)
    {
        Prompts.Clear();
        foreach (ConversionPrompt prompt in prompts)
            Prompts.Add(PromptInputViewModel.Create(prompt, dialogs));
    }
}