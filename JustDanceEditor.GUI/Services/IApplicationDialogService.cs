using JustDanceEditor.Conversion.Abstractions.Prompts;

namespace JustDanceEditor.GUI.Services;

public interface IApplicationDialogService
{
    Task<string?> PickFileAsync(string title, CancellationToken cancellationToken = default);

    Task<string?> PickSaveFileAsync(string title, string? suggestedFileName = null, CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    Task<bool> AskBooleanAsync(string title, string message, CancellationToken cancellationToken = default);

    Task<string?> AskChoiceAsync(string title, string message, IReadOnlyList<PromptOption> options, CancellationToken cancellationToken = default);

    void CloseMainWindow();
}
