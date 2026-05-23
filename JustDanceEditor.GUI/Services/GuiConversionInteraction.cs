using JustDanceEditor.Conversion.Abstractions.Prompts;

namespace JustDanceEditor.GUI.Services;

internal sealed class GuiConversionInteraction(IApplicationDialogService dialogs, PromptAnswerSet answers) : IConversionInteraction
{
    private readonly IApplicationDialogService _dialogs = dialogs;
    private readonly PromptAnswerSet _answers = answers;

    public async ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default)
    {
        PromptAnswerSet response = new();

        foreach (ConversionPrompt prompt in promptSet.Prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_answers.TryGetString(prompt.Id, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                response.Set(prompt.Id, value);
                continue;
            }

            if (prompt.Kind == ConversionPromptKind.Boolean)
            {
                bool result = await _dialogs.AskBooleanAsync(promptSet.Title, prompt.Label, cancellationToken);
                SetAnswer(response, prompt.Id, result ? "true" : "false");
                continue;
            }

            if (prompt.Kind == ConversionPromptKind.Choice && prompt.Options.Count > 0)
            {
                string? selected = await _dialogs.AskChoiceAsync(promptSet.Title, prompt.Label, prompt.Options, cancellationToken);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    SetAnswer(response, prompt.Id, selected);
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(prompt.DefaultValue))
            {
                SetAnswer(response, prompt.Id, prompt.DefaultValue);
                continue;
            }

            if (!prompt.Required)
            {
                SetAnswer(response, prompt.Id, string.Empty);
                continue;
            }

            throw new InvalidOperationException($"Missing required answer '{prompt.Id}' for prompt '{prompt.Label}'.");
        }

        return response;
    }

    private void SetAnswer(PromptAnswerSet response, string id, string value)
    {
        response.Set(id, value);
        _answers.Set(id, value);
    }
}
