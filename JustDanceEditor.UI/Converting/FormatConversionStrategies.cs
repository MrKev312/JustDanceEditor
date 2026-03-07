using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDNextPC;
using JustDanceEditor.Formats.UbiArt;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.Unity;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;

public interface IFormatConversionStrategy
{
    string FormatName { get; }
    IReadOnlyCollection<ConversionTarget> GetExportTargets();
    ConversionRequestBase CreateImportRequest(string inputPath, string outputPath, string? songName = null);
    ConversionRequestBase CreateExportRequest(string inputPath, string outputPath, ConversionTarget target, string? songName = null);
}

public sealed class JdiConversionStrategy : IFormatConversionStrategy
{
    private static readonly ConversionTarget[] Targets =
    [
        new("JDI", "jdi", "PC", "JDI", "Enter the folder where the JDI package should be written", 0)
    ];

    public string FormatName => "JDI";

    public IReadOnlyCollection<ConversionTarget> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(string inputPath, string outputPath, string? songName = null) =>
        new JdiConversionRequest(inputPath, outputPath);

    public ConversionRequestBase CreateExportRequest(string inputPath, string outputPath, ConversionTarget target, string? songName = null) =>
        new JdiConversionRequest(inputPath, outputPath);
}

public sealed class JDNextPCConversionStrategy : IFormatConversionStrategy
{
    private static readonly ConversionTarget[] Targets =
    [
        new("JDNext PC", "jdnextpc", "PC", "JDNext PC", "Enter the folder where the JDNext PC song folder should be written", 20)
    ];

    public string FormatName => "JDNext PC";

    public IReadOnlyCollection<ConversionTarget> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(string inputPath, string outputPath, string? songName = null) =>
        new JDNextPCConversionRequest(inputPath, outputPath);

    public ConversionRequestBase CreateExportRequest(string inputPath, string outputPath, ConversionTarget target, string? songName = null) =>
        new JDNextPCConversionRequest(inputPath, outputPath);
}

public sealed class UnityConversionStrategy : IFormatConversionStrategy
{
    private static readonly ConversionTarget[] Targets =
    [
        new("Unity", "unity-custom-server", "NX", "JD2023+ (Unity)", "Enter the Unity output root (custom server layout)", 0)
    ];

    public string FormatName => "Unity";

    public IReadOnlyCollection<ConversionTarget> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(string inputPath, string outputPath, string? songName = null)
    {
        return new UnityConversionRequest(inputPath, outputPath, outputPath)
        {
            ExportType = ExportType.CustomServer
        };
    }

    public ConversionRequestBase CreateExportRequest(string inputPath, string outputPath, ConversionTarget target, string? songName = null)
    {
        return new UnityConversionRequest(outputPath, outputPath, ResolveTemplatePath())
        {
            ExportType = ExportType.CustomServer
        };
    }

    private static string ResolveTemplatePath()
    {
        const string defaultTemplate = "./Template";
        if (Directory.Exists(defaultTemplate))
            return defaultTemplate;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Default template folder './Template' not found. Please specify the template path manually.");
        Console.ResetColor();
        return Question.AskFolder("Enter the template folder path", true);
    }
}

public sealed class UbiArtConversionStrategy : IFormatConversionStrategy
{
    private readonly Dictionary<string, UbiArtTargetDefinition> _targets;

    public UbiArtConversionStrategy()
    {
        _targets = BuildTargets().ToDictionary(target => target.Target.TargetId, StringComparer.OrdinalIgnoreCase);
    }

    public string FormatName => "UbiArt";

    public IReadOnlyCollection<ConversionTarget> GetExportTargets() => [.. _targets.Values.Select(value => value.Target)];

    public ConversionRequestBase CreateImportRequest(string inputPath, string outputPath, string? songName = null)
    {
        return new UbiArtConversionRequest(inputPath, outputPath, songName ?? ResolveSongName(inputPath));
    }

    public ConversionRequestBase CreateExportRequest(string inputPath, string outputPath, ConversionTarget target, string? songName = null)
    {
        if (!_targets.TryGetValue(target.TargetId, out UbiArtTargetDefinition? definition))
            throw new NotSupportedException($"Unknown UbiArt target '{target.TargetId}'.");

        return new UbiArtConversionRequest(inputPath, outputPath, songName ?? ResolveSongName(inputPath))
        {
            Type = definition.CookedType,
            ExportPlatform = definition.Platform,
            ExportEngineVersion = definition.EngineVersion
        };
    }

    private static IEnumerable<UbiArtTargetDefinition> BuildTargets()
    {
        yield return new UbiArtTargetDefinition(
            new ConversionTarget("UbiArt", "uncooked", "PC", "Uncooked (UbiArt)", "Enter the destination folder for the converted files", 10),
            UbiArtPlatform.Uncooked,
            UbiArtEngineVersion.JD2022,
            CookedType.Uncooked);

        yield return CreateVersionedTarget("pc-2017", "PC", "PC / JD2017 (UbiArt)", UbiArtPlatform.PC, UbiArtEngineVersion.JD2017, 30);

        foreach (int year in new[] { 2017, 2018, 2019, 2020, 2021, 2022 })
            yield return CreateVersionedTarget($"nx-{year}", "NX", $"NX / JD{year} (UbiArt)", UbiArtPlatform.NX, ToEngineVersion(year), 10 + (year - 2017));

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019 })
            yield return CreateVersionedTarget($"wiiu-{year}", "WiiU", $"WiiU / JD{year} (UbiArt)", UbiArtPlatform.WiiU, ToEngineVersion(year), 10 + (year - 2014));

        foreach (int year in new[] { 2014, 2015, 2016, 2017, 2018, 2019, 2020 })
            yield return CreateVersionedTarget($"wii-{year}", "Wii", $"Wii / JD{year} (UbiArt)", UbiArtPlatform.Wii, ToEngineVersion(year), 10 + (year - 2014));
    }

    private static UbiArtTargetDefinition CreateVersionedTarget(string targetId, string platformName, string displayName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, int sortOrder)
    {
        return new UbiArtTargetDefinition(
            new ConversionTarget("UbiArt", targetId, platformName, displayName, "Enter the destination folder for the converted files", sortOrder),
            platform,
            engineVersion,
            CookedType.Cooked);
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

    private static string? ResolveSongName(string inputPath)
    {
        string mapsPath = Path.Combine(inputPath, "world", "maps");
        if (!Directory.Exists(mapsPath))
            return null;

        string[] maps = Directory.GetDirectories(mapsPath);
        if (maps.Length == 0)
            return null;

        if (maps.Length == 1)
            return Path.GetFileName(maps[0]);

        string[] mapNames = [.. maps.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!)];
        if (mapNames.Length == 0)
            return null;

        int selection = Question.Ask(mapNames, 0, "Multiple maps found in direct filesystem. Which one should be converted?");
        return mapNames[selection];
    }

    private sealed record UbiArtTargetDefinition(
        ConversionTarget Target,
        UbiArtPlatform Platform,
        UbiArtEngineVersion EngineVersion,
        CookedType CookedType);
}