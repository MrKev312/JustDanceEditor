using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Formats.JDI.Conversion;

public sealed record ConversionRequestContext(
    string InputPath,
    string OutputPath,
    string? SongName = null,
    ConversionTargetDefinition? Target = null,
    PromptAnswerSet? Answers = null,
    IConversionInteraction? Interaction = null);
