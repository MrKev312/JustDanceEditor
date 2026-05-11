using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.IPK;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.AppHost;

public sealed class IpkToolProvider(ILogger<IpkToolProvider> logger) : IToolProvider
{
    private const string ExtractToolCode = "extract";
    private readonly ILogger<IpkToolProvider> _logger = logger;

    public string ProviderCode => "ipk";
    public string ProviderName => "IPK";
    public int Priority => 10;

    public IReadOnlyCollection<ToolDefinition> GetTools() =>
    [
        new(
            ProviderCode,
            ProviderName,
            ExtractToolCode,
            "Extract Archive",
            "Extract a Just Dance IPK archive into a folder.",
            [
                new ConversionPrompt(
                    ConversionPromptIds.InputPath,
                    ConversionPromptKind.FilePath,
                    "Enter the IPK archive path",
                    Required: true,
                    MustExist: true),
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the output folder",
                    Required: true)
            ],
            Priority: 10)
    ];

    public Task ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.Tool.ToolCode.Equals(ExtractToolCode, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unknown IPK tool '{context.Tool.ToolCode}'.");

        string inputPath = context.Answers.GetString(ConversionPromptIds.InputPath);
        string outputPath = context.Answers.GetString(ConversionPromptIds.OutputPath);
        Directory.CreateDirectory(outputPath);

        _logger.LogInformation("Extracting IPK archive '{InputPath}' into '{OutputPath}'", inputPath, outputPath);
        JustDanceIPKParser parser = new(inputPath, outputPath);
        parser.Parse(ShowInfo: true);
        _logger.LogInformation("Extracted IPK archive '{InputPath}' into '{OutputPath}'", inputPath, outputPath);

        return Task.CompletedTask;
    }
}
