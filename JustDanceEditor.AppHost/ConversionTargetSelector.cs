using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.AppHost;

public static class ConversionTargetSelector
{
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

    public static string FormatTargetLabel(ConversionTargetDefinition target, bool includeSupportStatus = false)
    {
        string suffix = includeSupportStatus
            ? target.SupportStatus switch
            {
                ConversionSupportStatus.Experimental => " (Experimental)",
                ConversionSupportStatus.KnownPartial => " (Known partial)",
                _ => string.Empty
            }
            : string.Empty;

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
