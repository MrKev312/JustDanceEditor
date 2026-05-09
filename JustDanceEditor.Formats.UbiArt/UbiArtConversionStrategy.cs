using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.UbiArt.Import;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtConversionStrategy : IFormatConversionStrategy
{
    private const string SongNamePromptId = "ubiart.songName";

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
        UbiArtConversionRequest request = new(context.InputPath, context.OutputPath, context.SongName);

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
        yield return new UbiArtTargetDefinition(
            new ConversionTargetDefinition(
                FormatCode: "ubiart",
                FormatName: "UbiArt",
                TargetCode: "uncooked",
                Platform: new PlatformDescriptor("pc", "PC"),
                Version: new TargetVersionDescriptor.Custom("uncooked", "Uncooked", 10),
                DisplayName: "Uncooked (UbiArt)",
                ExportPrompts: [OutputFolderPrompt()],
                Priority: 10),
            UbiArtPlatform.Uncooked,
            UbiArtEngineVersion.JD2022,
            CookedType.Uncooked);

        yield return CreateVersionedTarget("pc-2017", "pc", "PC", UbiArtPlatform.PC, UbiArtEngineVersion.JD2017, 2017);

        foreach (int year in new[] { 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"nx-{year}", "switch", "Switch", UbiArtPlatform.NX, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019 })
            yield return CreateVersionedTarget($"wiiu-{year}", "wiiu", "WiiU", UbiArtPlatform.WiiU, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019 })
            yield return CreateVersionedTarget($"x360-{year}", "xbox-360", "Xbox 360", UbiArtPlatform.X360, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"durango-{year}", "xbox-one", "Xbox One", UbiArtPlatform.Durango, ToEngineVersion(year), year);

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020 })
            yield return CreateVersionedTarget($"wii-{year}", "wii", "Wii", UbiArtPlatform.Wii, ToEngineVersion(year), year);
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
                SupportStatus: GetSupportStatus(platform)),
            platform,
            engineVersion,
            CookedType.Cooked);
    }

    private static ConversionSupportStatus GetSupportStatus(UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Wii => ConversionSupportStatus.Experimental,
        UbiArtPlatform.X360 => ConversionSupportStatus.Experimental,
        UbiArtPlatform.Durango => ConversionSupportStatus.KnownPartial,
        _ => ConversionSupportStatus.Stable
    };

    private static ConversionPrompt OutputFolderPrompt() =>
        new(
            ConversionPromptIds.OutputPath,
            ConversionPromptKind.FolderPath,
            "Enter the destination folder for the converted files",
            Required: false);

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
