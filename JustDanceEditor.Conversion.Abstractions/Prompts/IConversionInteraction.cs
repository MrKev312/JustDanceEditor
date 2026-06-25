namespace JustDanceEditor.Conversion.Abstractions.Prompts;

public interface IConversionInteraction
{
    ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default);
}