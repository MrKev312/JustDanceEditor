using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Cli;

internal sealed class SeededConversionInteraction(PromptAnswerSet seedAnswers, IConversionInteraction innerInteraction) : IConversionInteraction
{
    private readonly PromptAnswerSet _seedAnswers = seedAnswers;
    private readonly IConversionInteraction _innerInteraction = innerInteraction;

    public async ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default)
    {
        PromptAnswerSet answers = new();
        List<ConversionPrompt> missingPrompts = [];

        foreach (ConversionPrompt prompt in promptSet.Prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_seedAnswers.TryGetString(prompt.Id, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                answers.Set(prompt.Id, value);
                continue;
            }

            missingPrompts.Add(prompt);
        }

        if (missingPrompts.Count == 0)
            return answers;

        PromptAnswerSet askedAnswers = await _innerInteraction
            .AskAsync(new ConversionPromptSet(promptSet.Id, promptSet.Title, missingPrompts), cancellationToken)
            .ConfigureAwait(false);

        foreach (KeyValuePair<string, string> answer in askedAnswers.Answers)
            answers.Set(answer.Key, answer.Value);

        return answers;
    }
}
