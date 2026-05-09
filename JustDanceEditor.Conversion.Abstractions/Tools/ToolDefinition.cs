namespace JustDanceEditor.Conversion.Abstractions;

public sealed record ToolDefinition(
    string ProviderCode,
    string ProviderName,
    string ToolCode,
    string DisplayName,
    string Description,
    IReadOnlyList<ConversionPrompt>? Prompts = null,
    int Priority = 100)
{
    public IReadOnlyList<ConversionPrompt> Prompts { get; init; } = Prompts ?? [];
    public string FullCode => $"{ProviderCode}.{ToolCode}";
}

public sealed record ToolExecutionContext(
    ToolDefinition Tool,
    PromptAnswerSet Answers,
    IConversionInteraction? Interaction = null);

public interface IToolProvider
{
    string ProviderCode { get; }
    string ProviderName { get; }
    int Priority => 100;
    IReadOnlyCollection<ToolDefinition> GetTools();
    Task ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);
}
