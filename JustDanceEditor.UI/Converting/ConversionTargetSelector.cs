namespace JustDanceEditor.UI.Converting;

public sealed record ConversionTarget(
    string FormatName,
    string TargetId,
    string PlatformName,
    string DisplayName,
    string OutputPrompt,
    int SortOrder = 100);

public static class ConversionTargetSelector
{
    public static ConversionTarget AskTarget(IEnumerable<IFormatConversionStrategy> strategies, string prompt = "Select the target format/platform")
    {
        ConversionTarget[] allTargets = [.. strategies
            .SelectMany(strategy => strategy.GetExportTargets())
            .OrderBy(target => target.SortOrder)
            .ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)];

        if (allTargets.Length == 0)
            throw new InvalidOperationException("No export targets are available in this build.");

        string[] platforms = [.. allTargets
            .Select(target => target.PlatformName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPlatformOrder)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)];

        int platformSelection = Helpers.Question.Ask(platforms, 0, "Select the target platform");
        string selectedPlatform = platforms[platformSelection];

        ConversionTarget[] targets = [.. allTargets
            .Where(target => target.PlatformName.Equals(selectedPlatform, StringComparison.OrdinalIgnoreCase))
            .OrderBy(target => target.SortOrder)
            .ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)];

        string[] labels = [.. targets.Select(target => GetMenuLabel(target, selectedPlatform))];
        int selection = Helpers.Question.Ask(labels, 0, prompt);
        ConversionTarget chosenTarget = targets[selection];

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Selected: {chosenTarget.DisplayName}");
        Console.ResetColor();

        return chosenTarget;
    }

    private static int GetPlatformOrder(string platformName) => platformName switch
    {
        "PC" => 0,
        "NX" => 1,
        "WiiU" => 2,
        "Wii" => 3,
        _ => 100
    };

    private static string GetMenuLabel(ConversionTarget target, string selectedPlatform)
    {
        string prefix = selectedPlatform + " / ";
        return target.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? target.DisplayName[prefix.Length..]
            : target.DisplayName;
    }
}