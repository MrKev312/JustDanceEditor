using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtConversionStrategy : IFormatConversionStrategy
{
    private const string SongNamePromptId = "ubiart.songName";
    private const string RenderVideoSpeedTestPromptId = "ubiart.renderSpeedTest";

    private readonly Dictionary<string, UbiArtTargetDefinition> _targets;

    public UbiArtConversionStrategy()
    {
        _targets = BuildTargets().ToDictionary(target => target.Target.TargetCode, StringComparer.OrdinalIgnoreCase);
    }

    public string FormatCode => "ubiart";
    public string FormatName => "UbiArt";

    public IReadOnlyCollection<ConversionTargetDefinition> GetExportTargets() => [.. _targets.Values.Select(value => value.Target)];

    public ConversionRequestBase CreateImportRequest(ConversionRequestContext context)
    {
        UbiArtConversionRequest request = new(context.InputPath, context.OutputPath, context.SongName)
        {
            RenderVideoSpeedTest = ParseRenderVideoSpeedTest(context.Answers)
        };
        if (context.Interaction is not null)
        {
            request.SelectSongAsync = async names =>
            {
                PromptAnswerSet answers = await context.Interaction.AskAsync(new ConversionPromptSet(
                    "ubiart.selectSong",
                    "Multiple maps found",
                    [
                        new ConversionPrompt(
                            SongNamePromptId,
                            ConversionPromptKind.Choice,
                            "Multiple maps were found. Which one should be converted?",
                            Options: [.. names.Select(name => new PromptOption(name, name))])
                    ]));

                return answers.TryGetString(SongNamePromptId, out string? selected)
                    ? selected
                    : null;
            };
        }

        return request;
    }

    public ConversionRequestBase CreateExportRequest(ConversionRequestContext context)
    {
        ConversionTargetDefinition target = context.Target
            ?? throw new ArgumentException("UbiArt export requires a conversion target.", nameof(context));

        if (!_targets.TryGetValue(target.TargetCode, out UbiArtTargetDefinition? definition))
            throw new NotSupportedException($"Unknown UbiArt target '{target.TargetCode}'.");

        return new UbiArtConversionRequest(context.InputPath, context.OutputPath, context.SongName)
        {
            Type = definition.CookedType,
            ExportPlatform = definition.Platform,
            ExportEngineVersion = definition.EngineVersion
        };
    }

    private static IEnumerable<UbiArtTargetDefinition> BuildTargets()
    {
        yield return CreateUncookedTarget("uncooked-2014", "JD2014 Uncooked", UbiArtEngineVersion.JD2014, 2014);
        yield return CreateUncookedTarget("uncooked-2015", "JD2015 Uncooked", UbiArtEngineVersion.JD2015, 2015);
        yield return CreateUncookedTarget("uncooked-modern", "Modern Uncooked", UbiArtEngineVersion.JD2022, 2022);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020 })
            yield return CreateVersionedTarget($"wii-{year}", "wii", "Revolution (Wii)", UbiArtPlatform.Revolution, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019 })
            yield return CreateVersionedTarget($"wiiu-{year}", "wiiu", "Cafe (Wii U)", UbiArtPlatform.Cafe, ToEngineVersion(year), year);

        foreach (int year in new[] { 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"nx-{year}", "switch", "NX (Nintendo Switch)", UbiArtPlatform.NX, ToEngineVersion(year), year);

        yield return CreateVersionedTarget("pc-2017", "pc", "Win32 (PC)", UbiArtPlatform.Win32, UbiArtEngineVersion.JD2017, 2017);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018 })
            yield return CreateVersionedTarget($"ps3-{year}", "ps3", "Cell (PlayStation 3)", UbiArtPlatform.Cell, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019 })
            yield return CreateVersionedTarget($"x360-{year}", "xbox-360", "Xenon (Xbox 360)", UbiArtPlatform.Xenon, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"durango-{year}", "xbox-one", "Durango (Xbox One)", UbiArtPlatform.Durango, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"ps4-{year}", "ps4", "Orbis (PlayStation 4)", UbiArtPlatform.Orbis, ToEngineVersion(year), year);
    }

    private static UbiArtTargetDefinition CreateVersionedTarget(string targetCode, string platformCode, string platformName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, int year)
    {
        return new UbiArtTargetDefinition(
            new ConversionTargetDefinition(
                FormatCode: "ubiart",
                FormatName: "UbiArt",
                TargetCode: targetCode,
                Platform: new PlatformDescriptor(platformCode, platformName),
                Version: new TargetVersionDescriptor.SpecificYear(year),
                DisplayName: $"JD{year} (UbiArt)",
                ExportPrompts: [OutputFolderPrompt()],
                Priority: 10,
                SupportStatus: UbiArtSupportStatus.ForPlatform(platform)),
            platform,
            engineVersion,
            CookedType.Cooked);
    }

    private static UbiArtTargetDefinition CreateUncookedTarget(string targetCode, string displayName, UbiArtEngineVersion engineVersion, int year)
    {
        return new UbiArtTargetDefinition(
            new ConversionTargetDefinition(
                FormatCode: "ubiart",
                FormatName: "UbiArt",
                TargetCode: targetCode,
                Platform: new PlatformDescriptor("pc", "PC"),
                Version: new TargetVersionDescriptor.Custom(year.ToString(), $"JD{year}", year),
                DisplayName: displayName,
                ExportPrompts: [OutputFolderPrompt()],
                Priority: 10),
            UbiArtPlatform.Uncooked,
            engineVersion,
            CookedType.Uncooked);
    }

    private static ConversionPrompt OutputFolderPrompt() =>
        new(
            ConversionPromptIds.OutputPath,
            ConversionPromptKind.FolderPath,
            "Enter the destination folder for the converted files",
            Required: false);

    private static bool ParseRenderVideoSpeedTest(PromptAnswerSet? answers)
    {
        if (answers == null ||
            !answers.TryGetString(RenderVideoSpeedTestPromptId, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new ArgumentException(
                $"Unsupported UbiArt render speedtest value '{value}'. Use '{RenderVideoSpeedTestPromptId}=true' or '{RenderVideoSpeedTestPromptId}=false'.")
        };
    }

    private static UbiArtEngineVersion ToEngineVersion(int year) => year switch
    {
        2014 => UbiArtEngineVersion.JD2014,
        2015 => UbiArtEngineVersion.JD2015,
        2016 => UbiArtEngineVersion.JD2016,
        2017 => UbiArtEngineVersion.JD2017,
        2018 => UbiArtEngineVersion.JD2018,
        2019 => UbiArtEngineVersion.JD2019,
        2020 => UbiArtEngineVersion.JD2020,
        2021 => UbiArtEngineVersion.JD2021,
        2022 => UbiArtEngineVersion.JD2022,
        _ => throw new NotSupportedException($"JD{year} is not a supported UbiArt export target.")
    };

    private sealed record UbiArtTargetDefinition(
        ConversionTargetDefinition Target,
        UbiArtPlatform Platform,
        UbiArtEngineVersion EngineVersion,
        CookedType CookedType);
}
