using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.AppHost;

public static class ConsoleConversionTargetSelector
{
    public static ConversionTargetDefinition AskTarget(
        IEnumerable<IFormatConversionStrategy> strategies,
        Func<IReadOnlyList<string>, int, string, int> ask,
        string prompt = "Select the target format/platform")
    {
        ConversionTargetDefinition[] allTargets = ConversionTargetSelector.GetAvailableTargets(strategies);

        if (allTargets.Length == 0)
            throw new InvalidOperationException("No export targets are available in this build.");

        PlatformDescriptor[] platforms = ConversionTargetSelector.GetSortedPlatforms(allTargets);

        int platformSelection = ask([.. platforms.Select(platform => platform.DisplayName)], 0, "Select the target platform");
        PlatformDescriptor selectedPlatform = platforms[platformSelection];

        ConversionTargetDefinition[] targets = ConversionTargetSelector.GetSortedTargetsForPlatform(allTargets, selectedPlatform.PlatformCode);

        string[] labels = [.. targets.Select(target => ConversionTargetSelector.FormatTargetLabel(target, includeSupportStatus: true))];
        int selection = ask(labels, 0, prompt);
        return targets[selection];
    }
}