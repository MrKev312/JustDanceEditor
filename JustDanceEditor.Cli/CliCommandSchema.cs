using JustDanceEditor.Conversion.Abstractions.Prompts;

using System.CommandLine;

namespace JustDanceEditor.Cli;

[Flags]
internal enum CliOptionProfile
{
    None = 0,
    Headless = 1 << 0,
    Input = 1 << 1,
    Output = 1 << 2,
    Target = 1 << 3,
    Source = 1 << 4,
    Song = 1 << 5,
    Answer = 1 << 6,
    DownloadOnlineAssets = 1 << 7,
    Provider = 1 << 8,
    Platform = 1 << 9,
    Id = 1 << 10,
    Tool = 1 << 11,
    Force = 1 << 12,
    NoWait = 1 << 13,
    Encoding = 1 << 14,
    AudioEncoding = 1 << 15,
    TextureEncoding = 1 << 16,
    Speedtest = 1 << 17,

    DroppedPath = Headless | Output | Force | NoWait | Encoding | AudioEncoding | TextureEncoding,
    JdiConversion = Headless | Input | Output | Target | Source | Song | Answer | DownloadOnlineAssets | Speedtest,
    IpkTool = Headless | Input | Output | Force,
    MediaConversion = Headless | Input | Output | Force | Encoding,
    ToolExecution = Headless | Input | Output | Force | Answer | Id | Tool,
}

internal sealed class CliCommandSymbols
{
    public Option<bool> HeadlessOption { get; } = new("--headless", "--non-interactive", "-n")
    {
        Description = "Run as a strict non-interactive CLI."
    };

    public Option<string?> InputOption { get; } = new("--input", "-i")
    {
        Description = "Input file or folder."
    };

    public Option<string?> OutputOption { get; } = new("--output", "-o")
    {
        Description = "Output file or folder."
    };

    public Option<string?> TargetOption { get; } = new("--target", "-t")
    {
        Description = "Conversion target code."
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Force source format by code or display name."
    };

    public Option<string?> SongOption { get; } = new("--song")
    {
        Description = "Select a specific map/song when a source contains several."
    };

    public Option<string[]> AnswerOption { get; } = new("--answer")
    {
        Description = "Supply a prompt answer as id=value. Can be repeated.",
        Arity = ArgumentArity.ZeroOrMore
    };

    public Option<bool> DownloadOnlineAssetsOption { get; } = new("--download-online-assets")
    {
        Description = "Download online assets after import."
    };

    public Option<bool> SpeedtestOption { get; } = new("--speedtest", "--render-speedtest")
    {
        Description = "Render cinematic video frames and discard the output stream instead of encoding video."
    };

    public Option<string?> ProviderOption { get; } = new("--provider")
    {
        Description = "Filter by tool provider or platform."
    };

    public Option<string?> PlatformOption { get; } = new("--platform")
    {
        Description = "Filter targets by platform."
    };

    public Option<string?> IdOption { get; } = new("--id")
    {
        Description = "Tool id."
    };

    public Option<string?> ToolOption { get; } = new("--tool")
    {
        Description = "Tool id."
    };

    public Option<bool> ForceOption { get; } = new("--force", "-f")
    {
        Description = "Overwrite existing output where supported."
    };

    public Option<bool> NoWaitOption { get; } = new("--no-wait")
    {
        Description = "Do not wait for a key after dropped-path processing."
    };

    public Option<string?> EncodingOption { get; } = new("--encoding", "--target-encoding", "--target-format")
    {
        Description = "Target audio/image/texture encoding."
    };

    public Option<string?> AudioEncodingOption { get; } = new("--audio-encoding")
    {
        Description = "Target audio encoding for mixed dropped-path batches."
    };

    public Option<string?> TextureEncodingOption { get; } = new("--texture-encoding", "--image-encoding")
    {
        Description = "Target image/texture encoding for mixed dropped-path batches."
    };

    public Argument<string[]> PathsArgument { get; } = new("path")
    {
        Description = "Dropped files or folders.",
        Arity = ArgumentArity.ZeroOrMore
    };

    public Argument<string?> ToolCodeArgument { get; } = new("tool-code")
    {
        Description = "Provider tool code, for example ipk.extract.",
        Arity = ArgumentArity.ZeroOrOne
    };

    public Dictionary<string, Option<string?>> DynamicPromptOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
}