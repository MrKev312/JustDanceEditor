using JustDanceEditor.Conversion.Abstractions.Prompts;

namespace JustDanceEditor.GUI.ViewModels.Prompts;

public static class PromptInputAnswerBuilder
{
    public static PromptAnswerSet Build(IEnumerable<PromptInputViewModel> inputs)
    {
        PromptAnswerSet answers = new();
        foreach (PromptInputViewModel input in inputs)
        {
            if (input.ShouldInclude)
                answers.Set(input.Id, input.Value);
        }

        return answers;
    }
}