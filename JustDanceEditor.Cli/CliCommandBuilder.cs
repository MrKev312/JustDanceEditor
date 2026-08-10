using JustDanceEditor.Cli.Interactive;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;

using System.CommandLine;
using System.CommandLine.Help;

namespace JustDanceEditor.Cli;

internal sealed class CliCommandBuilder(
    CliApp app,
    IEnumerable<IToolProvider> toolProviders,
    DroppedPathProcessor droppedPathProcessor,
    ConsoleApp consoleApp)
{
    public Command BuildRootCommand()
    {
        CliCommandSymbols symbols = new();
        Command rootCommand = new("JustDanceEditor.Cli", "Just Dance Editor command-line and interactive console.")
        {
            TreatUnmatchedTokensAsErrors = true
        };
        AddHelp(rootCommand);
        AddOptions(rootCommand, symbols, CliOptionProfile.DroppedPath);
        rootCommand.Arguments.Add(symbols.PathsArgument);

        rootCommand.SetAction(parseResult => CliApp.ExecuteCommand(() =>
        {
            CliOptions options = CreateOptions(parseResult, symbols);
            string[] paths = parseResult.GetValue(symbols.PathsArgument) ?? [];
            if (paths.Length > 0)
            {
                return droppedPathProcessor.ProcessDroppedPaths(paths, new DroppedPathOptions(
                    Headless: options.Headless,
                    OutputPath: options.Get("output"),
                    Force: options.HasFlag("force"),
                    WaitForKey: !options.HasFlag("no-wait"),
                    AudioEncoding: options.Get("audio-encoding") ?? options.Get("encoding"),
                    TextureEncoding: options.Get("texture-encoding") ?? options.Get("encoding")));
            }

            if (options.Headless)
            {
                Console.WriteLine("Missing command or dropped path. Run '--help' for usage.");
                return 1;
            }

            consoleApp.Run();
            return 0;
        }));

        Command helpCommand = new("help", "Show help and usage information.");
        helpCommand.SetAction(_ => rootCommand.Parse(["--help"]).Invoke());
        rootCommand.Subcommands.Add(helpCommand);

        rootCommand.Subcommands.Add(CreateCommand("plugins", "List loaded converter plugins, formats, targets, and assembly diagnostics.", symbols, CliOptionProfile.Headless, app.ListPlugins, "list-plugins"));
        rootCommand.Subcommands.Add(CreateCommand("targets", "List export targets grouped by platform.", symbols, CliOptionProfile.Headless | CliOptionProfile.Platform, app.ListTargets, "list-targets"));
        rootCommand.Subcommands.Add(CreateCommand("tools", "List tool providers and available tools.", symbols, CliOptionProfile.Headless | CliOptionProfile.Provider, app.ListTools, "list-tools"));
        rootCommand.Subcommands.Add(CreateCommand("convert", "Convert one source to a target through JDI.", symbols, CliOptionProfile.JdiConversion, app.ConvertSingle));
        rootCommand.Subcommands.Add(CreateCommand("batch", "Convert every detected source in a folder.", symbols, CliOptionProfile.JdiConversion, app.ConvertBatch, "batch-convert"));
        rootCommand.Subcommands.Add(CreateCommand("extract-ipk", "Extract an IPK archive.", symbols, CliOptionProfile.IpkTool, options => app.RunToolByCode("ipk.extract", options)));
        rootCommand.Subcommands.Add(CreateCommand("pack-ipk", "Pack a folder into an IPK archive.", symbols, CliOptionProfile.IpkTool, app.PackIpk));
        rootCommand.Subcommands.Add(CreateCommand("rebuild-secure-fat", "Rebuild secure_fat.gf from platform IPK archives.", symbols, CliOptionProfile.Headless | CliOptionProfile.Input | CliOptionProfile.Platform, app.RebuildSecureFat));
        rootCommand.Subcommands.Add(CreateCommand("audio", "Convert audio files to a selected target encoding.", symbols, CliOptionProfile.MediaConversion, app.ConvertAudio, "convert-audio"));
        rootCommand.Subcommands.Add(CreateCommand("texture", "Convert image and texture files to a selected target encoding.", symbols, CliOptionProfile.MediaConversion, app.ConvertTexture, "convert-texture"));
        rootCommand.Subcommands.Add(CreateCommand("cache-spread", "Spread cache folders for exFAT.", symbols, CliOptionProfile.Headless | CliOptionProfile.Input | CliOptionProfile.Force, options => app.RunToolByCode("unity.cache-spread", options)));
        rootCommand.Subcommands.Add(CreateToolCommand(symbols));

        return rootCommand;
    }

    private static Command CreateCommand(string name, string description, CliCommandSymbols symbols, CliOptionProfile options, Func<CliOptions, int> action, params string[] aliases)
    {
        Command command = new(name, description);
        foreach (string alias in aliases)
            command.Aliases.Add(alias);

        AddHelp(command);
        AddOptions(command, symbols, options);
        command.SetAction(parseResult => CliApp.ExecuteCommand(() => action(CreateOptions(parseResult, symbols))));
        return command;
    }

    private Command CreateToolCommand(CliCommandSymbols symbols)
    {
        Command command = new("tool", "Run a registered provider tool.");
        command.Aliases.Add("run-tool");
        AddHelp(command);
        AddOptions(command, symbols, CliOptionProfile.ToolExecution);
        command.Arguments.Add(symbols.ToolCodeArgument);

        foreach (string promptId in toolProviders
            .SelectMany(provider => provider.GetTools())
            .SelectMany(tool => tool.Prompts)
            .Select(prompt => prompt.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(promptId => !IsCommonOptionName(promptId)))
        {
            Option<string?> promptOption = new($"--{promptId}")
            {
                Description = $"Answer for prompt '{promptId}'."
            };
            symbols.DynamicPromptOptions[promptId] = promptOption;
            command.Options.Add(promptOption);
        }

        command.SetAction(parseResult => CliApp.ExecuteCommand(() => app.RunToolCommand(parseResult, symbols)));
        return command;
    }

    private static void AddOptions(Command command, CliCommandSymbols symbols, CliOptionProfile options)
    {
        if (options.HasFlag(CliOptionProfile.Headless))
            command.Options.Add(symbols.HeadlessOption);
        if (options.HasFlag(CliOptionProfile.Input))
            command.Options.Add(symbols.InputOption);
        if (options.HasFlag(CliOptionProfile.Output))
            command.Options.Add(symbols.OutputOption);
        if (options.HasFlag(CliOptionProfile.Target))
            command.Options.Add(symbols.TargetOption);
        if (options.HasFlag(CliOptionProfile.Source))
            command.Options.Add(symbols.SourceOption);
        if (options.HasFlag(CliOptionProfile.Song))
            command.Options.Add(symbols.SongOption);
        if (options.HasFlag(CliOptionProfile.Answer))
            command.Options.Add(symbols.AnswerOption);
        if (options.HasFlag(CliOptionProfile.DownloadOnlineAssets))
            command.Options.Add(symbols.DownloadOnlineAssetsOption);
        if (options.HasFlag(CliOptionProfile.Speedtest))
            command.Options.Add(symbols.SpeedtestOption);
        if (options.HasFlag(CliOptionProfile.Provider))
            command.Options.Add(symbols.ProviderOption);
        if (options.HasFlag(CliOptionProfile.Platform))
            command.Options.Add(symbols.PlatformOption);
        if (options.HasFlag(CliOptionProfile.Id))
            command.Options.Add(symbols.IdOption);
        if (options.HasFlag(CliOptionProfile.Tool))
            command.Options.Add(symbols.ToolOption);
        if (options.HasFlag(CliOptionProfile.Force))
            command.Options.Add(symbols.ForceOption);
        if (options.HasFlag(CliOptionProfile.NoWait))
            command.Options.Add(symbols.NoWaitOption);
        if (options.HasFlag(CliOptionProfile.Encoding))
            command.Options.Add(symbols.EncodingOption);
        if (options.HasFlag(CliOptionProfile.AudioEncoding))
            command.Options.Add(symbols.AudioEncodingOption);
        if (options.HasFlag(CliOptionProfile.TextureEncoding))
            command.Options.Add(symbols.TextureEncodingOption);
    }

    private static void AddHelp(Command command)
    {
        command.Options.Add(new HelpOption("-h", "--help"));
    }

    public static CliOptions CreateOptions(ParseResult parseResult, CliCommandSymbols symbols)
    {
        CliOptions options = new(GetValue(parseResult, symbols.HeadlessOption));
        options.Set("input", GetValue(parseResult, symbols.InputOption));
        options.Set("output", GetValue(parseResult, symbols.OutputOption));
        options.Set("target", GetValue(parseResult, symbols.TargetOption));
        options.Set("source", GetValue(parseResult, symbols.SourceOption));
        options.Set("song", GetValue(parseResult, symbols.SongOption));
        options.Set("answer", GetValue(parseResult, symbols.AnswerOption) ?? []);
        options.Set("provider", GetValue(parseResult, symbols.ProviderOption));
        options.Set("platform", GetValue(parseResult, symbols.PlatformOption));
        options.Set("id", GetValue(parseResult, symbols.IdOption));
        options.Set("tool", GetValue(parseResult, symbols.ToolOption));
        options.Set("encoding", GetValue(parseResult, symbols.EncodingOption));
        options.Set("audio-encoding", GetValue(parseResult, symbols.AudioEncodingOption));
        options.Set("texture-encoding", GetValue(parseResult, symbols.TextureEncodingOption));
        options.SetFlag("download-online-assets", GetValue(parseResult, symbols.DownloadOnlineAssetsOption));
        options.SetFlag("speedtest", GetValue(parseResult, symbols.SpeedtestOption));
        options.SetFlag("force", GetValue(parseResult, symbols.ForceOption));
        options.SetFlag("no-wait", GetValue(parseResult, symbols.NoWaitOption));

        foreach ((string promptId, Option<string?> promptOption) in symbols.DynamicPromptOptions)
            options.Set(promptId, GetValue(parseResult, promptOption));

        return options;
    }

    private static TValue? GetValue<TValue>(ParseResult parseResult, Option<TValue> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
        catch (ArgumentException)
        {
            return default;
        }
    }

    private static bool IsCommonOptionName(string promptId)
    {
        string[] common =
        [
            "input",
            "output",
            "target",
            "source",
            "song",
            "answer",
            "provider",
            "platform",
            "id",
            "tool",
            "force",
            "encoding",
            "audio-encoding",
            "texture-encoding",
            ConversionPromptIds.InputPath,
            ConversionPromptIds.OutputPath,
            ConversionPromptIds.Force,
        ];

        return common.Any(option => option.Equals(promptId, StringComparison.OrdinalIgnoreCase));
    }
}
