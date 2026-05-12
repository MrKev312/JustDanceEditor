using JustDanceEditor.Conversion.Abstractions;

using KevInc.UbiArt.Ipk;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.AppHost;

public sealed class IpkToolProvider(ILogger<IpkToolProvider> logger) : IToolProvider
{
    private const string ExtractToolCode = "extract";
    private const string PackToolCode = "pack";
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
            "Extract a UbiArt IPK archive into a folder.",
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
            Priority: 10),
        new(
            ProviderCode,
            ProviderName,
            PackToolCode,
            "Pack Archive",
            "Pack a folder into a UbiArt IPK archive.",
            [
                new ConversionPrompt(
                    ConversionPromptIds.InputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the source folder path",
                    Required: true,
                    MustExist: true),
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FilePath,
                    "Enter the output IPK path",
                    Required: true)
            ],
            Priority: 20)
    ];

    public Task ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Tool.ToolCode.Equals(ExtractToolCode, StringComparison.OrdinalIgnoreCase))
        {
            Extract(context);
            return Task.CompletedTask;
        }

        if (context.Tool.ToolCode.Equals(PackToolCode, StringComparison.OrdinalIgnoreCase))
        {
            Pack(context);
            return Task.CompletedTask;
        }

        throw new NotSupportedException($"Unknown IPK tool '{context.Tool.ToolCode}'.");
    }

    private void Extract(ToolExecutionContext context)
    {
        string inputPath = context.Answers.GetString(ConversionPromptIds.InputPath);
        string outputPath = context.Answers.GetString(ConversionPromptIds.OutputPath);
        Directory.CreateDirectory(outputPath);

        _logger.LogInformation("Extracting IPK archive '{InputPath}' into '{OutputPath}'", inputPath, outputPath);
        UbiArtIpkParser parser = new(inputPath, outputPath);
        parser.Parse(ShowInfo: true);
        _logger.LogInformation("Extracted IPK archive '{InputPath}' into '{OutputPath}'", inputPath, outputPath);
    }

    private void Pack(ToolExecutionContext context)
    {
        string inputPath = context.Answers.GetString(ConversionPromptIds.InputPath);
        string outputPath = context.Answers.GetString(ConversionPromptIds.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

        _logger.LogInformation("Packing folder '{InputPath}' into IPK archive '{OutputPath}'", inputPath, outputPath);
        UbiArtIpkWriter writer = new(inputPath, outputPath);
        writer.Pack();
        _logger.LogInformation("Packed folder '{InputPath}' into IPK archive '{OutputPath}'", inputPath, outputPath);
    }
}
