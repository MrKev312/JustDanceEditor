using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.GUI.Services;

using System.Collections.ObjectModel;

namespace JustDanceEditor.GUI.ViewModels.Prompts;

public abstract class PromptInputViewModel(ConversionPrompt prompt) : ViewModelBase
{
    public ConversionPrompt Prompt { get; } = prompt;

    public string Id => Prompt.Id;

    public string Label => Prompt.Label;

    public bool Required => Prompt.Required;

    public abstract string Value { get; }

    public bool ShouldInclude => !string.IsNullOrWhiteSpace(Value)
        || Required
        || !string.IsNullOrWhiteSpace(Prompt.DefaultValue);

    public static PromptInputViewModel Create(ConversionPrompt prompt, IApplicationDialogService dialogs)
    {
        return prompt.Kind switch
        {
            ConversionPromptKind.Boolean => new BooleanPromptInputViewModel(prompt),
            ConversionPromptKind.Choice => new ChoicePromptInputViewModel(prompt),
            ConversionPromptKind.FilePath or ConversionPromptKind.FolderPath => new PathPromptInputViewModel(prompt, dialogs),
            _ => new TextPromptInputViewModel(prompt)
        };
    }
}

public sealed partial class TextPromptInputViewModel(ConversionPrompt prompt) : PromptInputViewModel(prompt)
{
    [ObservableProperty]
    public partial string Text { get; set; } = prompt.DefaultValue ?? string.Empty;

    public string PlaceholderText => Required ? "Required" : "Optional";

    public override string Value => Text.Trim();
}

public sealed partial class BooleanPromptInputViewModel(ConversionPrompt prompt) : PromptInputViewModel(prompt)
{
    [ObservableProperty]
    public partial bool IsChecked { get; set; } = bool.TryParse(prompt.DefaultValue, out bool defaultValue) && defaultValue;

    public override string Value => IsChecked ? "true" : "false";
}

public sealed partial class ChoicePromptInputViewModel : PromptInputViewModel
{
    public ChoicePromptInputViewModel(ConversionPrompt prompt)
        : base(prompt)
    {
        Options = [.. prompt.Options.Select(option => new PromptOptionItemViewModel(option))];
        SelectedOption = Options.FirstOrDefault(option =>
                option.Option.Value.Equals(prompt.DefaultValue, StringComparison.OrdinalIgnoreCase))
            ?? Options.FirstOrDefault();
    }

    public ObservableCollection<PromptOptionItemViewModel> Options { get; }

    [ObservableProperty]
    public partial PromptOptionItemViewModel? SelectedOption { get; set; }

    public override string Value => SelectedOption?.Option.Value ?? Prompt.DefaultValue ?? string.Empty;
}

public sealed partial class PathPromptInputViewModel(ConversionPrompt prompt, IApplicationDialogService dialogs) : PromptInputViewModel(prompt)
{
    [ObservableProperty]
    public partial string Path { get; set; } = prompt.DefaultValue ?? string.Empty;

    public bool IsFile => Prompt.Kind == ConversionPromptKind.FilePath;

    public bool IsOutputFile => IsFile && Id.Equals(ConversionPromptIds.OutputPath, StringComparison.OrdinalIgnoreCase);

    public string BrowseLabel => IsOutputFile ? "Save" : IsFile ? "File" : "Folder";

    public string PlaceholderText => IsOutputFile ? "Output file path" : IsFile ? "File path" : "Folder path";

    public override string Value => Path.Trim();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? path;
        if (IsOutputFile)
        {
            string? suggestedFileName = string.IsNullOrWhiteSpace(Path) ? null : System.IO.Path.GetFileName(Path);
            path = await dialogs.PickSaveFileAsync(Label, suggestedFileName, CancellationToken.None);
        }
        else
        {
            path = IsFile
                ? await dialogs.PickFileAsync(Label, CancellationToken.None)
                : await dialogs.PickFolderAsync(Label, CancellationToken.None);
        }

        if (path is not null)
            Path = path;
    }

    [RelayCommand]
    private void SetPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            Path = path;
    }
}