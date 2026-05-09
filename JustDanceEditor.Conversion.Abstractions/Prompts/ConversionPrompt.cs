namespace JustDanceEditor.Conversion.Abstractions;

public enum ConversionPromptKind
{
    Text,
    Integer,
    Boolean,
    Choice,
    MultiChoice,
    FilePath,
    FolderPath
}

public sealed record ConversionPrompt(
    string Id,
    ConversionPromptKind Kind,
    string Label,
    bool Required = true,
    string? DefaultValue = null,
    IReadOnlyList<PromptOption>? Options = null,
    bool MustExist = false)
{
    public IReadOnlyList<PromptOption> Options { get; init; } = Options ?? [];
}

public sealed record PromptOption(string Value, string Label);

public sealed record ConversionPromptSet(
    string Id,
    string Title,
    IReadOnlyList<ConversionPrompt> Prompts);

public static class ConversionPromptIds
{
    public const string OutputPath = "outputPath";
}
