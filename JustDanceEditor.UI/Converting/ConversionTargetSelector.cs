using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.UI.Converting;

public static class ConversionTargetSelector
{
    public static ConversionTargetDefinition AskTarget(IEnumerable<IFormatConversionStrategy> strategies, string prompt = "Select the target format/platform")
    {
        ConversionTargetDefinition[] allTargets = GetAvailableTargets(strategies);

        if (allTargets.Length == 0)
            throw new InvalidOperationException("No export targets are available in this build.");

        PlatformDescriptor[] platforms = GetSortedPlatforms(allTargets);

        int platformSelection = Helpers.Question.Ask([.. platforms.Select(platform => platform.DisplayName)], 0, "Select the target platform");
        PlatformDescriptor selectedPlatform = platforms[platformSelection];

        ConversionTargetDefinition[] targets = GetSortedTargetsForPlatform(allTargets, selectedPlatform.PlatformCode);

        string[] labels = [.. targets.Select(FormatTargetLabel)];
        int selection = Helpers.Question.Ask(labels, 0, prompt);
        ConversionTargetDefinition chosenTarget = targets[selection];

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Selected: {FormatTargetLabel(chosenTarget)}");
        Console.ResetColor();

        return chosenTarget;
    }

    public static ConversionTargetDefinition[] GetAvailableTargets(IEnumerable<IFormatConversionStrategy> strategies)
    {
        return [.. MergeTargets(strategies
            .SelectMany(strategy => strategy.GetExportTargets())
            .OrderBy(target => target.Priority)
            .ThenBy(target => target.FormatName, StringComparer.OrdinalIgnoreCase))];
    }

    public static PlatformDescriptor[] GetSortedPlatforms(IEnumerable<ConversionTargetDefinition> targets)
    {
        return [.. targets
            .Select(target => target.Platform)
            .DistinctBy(platform => platform.PlatformCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(platform => platform.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public static ConversionTargetDefinition[] GetSortedTargetsForPlatform(IEnumerable<ConversionTargetDefinition> targets, string platformCode)
    {
        return [.. targets
            .Where(target => target.Platform.PlatformCode.Equals(platformCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(target => IsVersionedTarget(target.Version) ? 0 : 1)
            .ThenBy(target => IsVersionedTarget(target.Version) ? target.Version.SortOrder : 0)
            .ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public static string FormatTargetLabel(ConversionTargetDefinition target)
    {
        string suffix = target.SupportStatus switch
        {
            ConversionSupportStatus.Experimental => " (Experimental)",
            ConversionSupportStatus.KnownPartial => " (Known partial)",
            _ => string.Empty
        };

        return $"{target.DisplayName}{suffix}";
    }

    private static IEnumerable<ConversionTargetDefinition> MergeTargets(IEnumerable<ConversionTargetDefinition> targets)
    {
        Dictionary<string, ConversionTargetDefinition> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ConversionTargetDefinition target in targets)
        {
            string key = $"{target.Platform.PlatformCode}:{GetVersionMergeKey(target.Version)}";
            if (seen.TryAdd(key, target))
                yield return target;
        }
    }

    private static string GetVersionMergeKey(TargetVersionDescriptor version) => version switch
    {
        TargetVersionDescriptor.SpecificYear specific => $"year:{specific.Year}",
        TargetVersionDescriptor.OpenEnded openEnded => $"open:{openEnded.StartYear}",
        TargetVersionDescriptor.Custom custom => $"custom:{custom.Code}",
        TargetVersionDescriptor.None none => $"none:{none.Label}",
        TargetVersionDescriptor.YearSet set => $"years:{string.Join(',', set.Years)}",
        _ => version.Label
    };

    private static bool IsVersionedTarget(TargetVersionDescriptor version) => version switch
    {
        TargetVersionDescriptor.SpecificYear => true,
        TargetVersionDescriptor.OpenEnded => true,
        TargetVersionDescriptor.YearSet set when set.Years.Count > 0 => true,
        _ => false
    };
}
