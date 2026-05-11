using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.GUI;

public interface IApplicationDialogService
{
    Task<string?> PickFileAsync(string title, CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    Task<bool> AskBooleanAsync(string title, string message, CancellationToken cancellationToken = default);

    Task<string?> AskChoiceAsync(string title, string message, IReadOnlyList<PromptOption> options, CancellationToken cancellationToken = default);

    void CloseMainWindow();
}
