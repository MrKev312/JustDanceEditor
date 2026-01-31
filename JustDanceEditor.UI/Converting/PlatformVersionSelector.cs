using JustDanceEditor.Formats.UbiArt.Import;

namespace JustDanceEditor.UI.Converting;

/// <summary>
/// Represents a target platform and version selection for conversion.
/// </summary>
public sealed class TargetSelection
{
    /// <summary>
    /// The target platform.
    /// </summary>
    public required TargetPlatform Platform { get; init; }

    /// <summary>
    /// The JD version year (e.g., 2017, 2022, 2023). Null for versionless targets.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Gets whether this target is just the JDI intermediate format.
    /// </summary>
    public bool IsJdi => Platform == TargetPlatform.JDI;

    /// <summary>
    /// Gets whether this target uses the Unity engine (JD 2023+).
    /// </summary>
    public bool IsUnityEngine => Version >= 2023;

    /// <summary>
    /// Gets whether this target uses the UbiArt engine (JD 2014-2022).
    /// </summary>
    public bool IsUbiArtEngine => !IsJdi && Version is null or (>= 2014 and <= 2022);

    /// <summary>
    /// Converts to UbiArt platform enum.
    /// </summary>
    public UbiArtPlatform ToUbiArtPlatform() => Platform switch
    {
        TargetPlatform.PC => UbiArtPlatform.PC,
        TargetPlatform.Uncooked => UbiArtPlatform.Uncooked,
        TargetPlatform.NX => UbiArtPlatform.NX,
        TargetPlatform.WiiU => UbiArtPlatform.WiiU,
        TargetPlatform.Wii => UbiArtPlatform.Wii,
        _ => throw new NotSupportedException($"Platform {Platform} is not a UbiArt platform.")
    };

    /// <summary>
    /// Converts to UbiArt engine version enum.
    /// </summary>
    public UbiArtEngineVersion ToUbiArtEngineVersion() => Version switch
    {
        null => UbiArtEngineVersion.JD2022, // Default for uncooked
        2014 => UbiArtEngineVersion.JD2014,
        2015 => UbiArtEngineVersion.JD2015,
        2016 => UbiArtEngineVersion.JD2016,
        2017 => UbiArtEngineVersion.JD2017,
        2018 => UbiArtEngineVersion.JD2018,
        2019 => UbiArtEngineVersion.JD2019,
        2020 => UbiArtEngineVersion.JD2020,
        2021 => UbiArtEngineVersion.JD2021,
        2022 => UbiArtEngineVersion.JD2022,
        _ => throw new NotSupportedException($"Version {Version} is not a valid UbiArt version.")
    };

    /// <summary>
    /// Gets the format name (JDI, Unity or UbiArt).
    /// </summary>
    public string FormatName => IsJdi ? "JDI" : IsUnityEngine ? "Unity" : "UbiArt";
}

/// <summary>
/// Target platforms available for export.
/// </summary>
public enum TargetPlatform
{
    JDI,
    Uncooked,
    PC,
    NX,
    WiiU,
    Wii
}

/// <summary>
/// Helper for platform and version selection in the UI.
/// </summary>
public static class PlatformVersionSelector
{
    /// <summary>
    /// Gets the available JD versions for a given platform.
    /// </summary>
    public static int[] GetVersionsForPlatform(TargetPlatform platform) => platform switch
    {
        TargetPlatform.JDI => [], // JDI is versionless
        TargetPlatform.Uncooked => [], // Uncooked is versionless
        TargetPlatform.PC => [2017], // Only one version available
        TargetPlatform.Wii => [2014, 2015, 2016, 2017, 2018, 2019, 2020],
        TargetPlatform.WiiU => [2014, 2015, 2016, 2017, 2018, 2019],
        TargetPlatform.NX => [2017, 2018, 2019, 2020, 2021, 2022, 2023],
        _ => []
    };

    /// <summary>
    /// Asks the user to select a target platform.
    /// </summary>
    public static TargetPlatform AskPlatform(string prompt = "Select the target format/platform")
    {
        string[] platforms = ["JDI", "Uncooked", "PC", "NX", "WiiU", "Wii"];
        int selection = Helpers.Question.Ask(platforms, 0, prompt);
        return selection switch
        {
            0 => TargetPlatform.JDI,
            1 => TargetPlatform.Uncooked,
            2 => TargetPlatform.PC,
            3 => TargetPlatform.NX,
            4 => TargetPlatform.WiiU,
            5 => TargetPlatform.Wii,
            _ => throw new InvalidOperationException("Invalid selection")
        };
    }

    /// <summary>
    /// Asks the user to select a JD version for the given platform.
    /// Returns null if the platform is versionless or has only one version.
    /// </summary>
    public static int? AskVersion(TargetPlatform platform, string prompt = "Select the JD version")
    {
        int[] versions = GetVersionsForPlatform(platform);

        if (versions.Length == 0)
            return null; // Versionless (JDI, Uncooked)

        if (versions.Length == 1)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Auto-selected version: JD{versions[0]} (only available version for {platform})");
            Console.ResetColor();
            return versions[0];
        }

        string[] versionLabels = versions.Select(v => v == 2023 ? "JD2023+ (Unity)" : $"JD{v}").ToArray();
        int selection = Helpers.Question.Ask(versionLabels, 0, prompt);
        return versions[selection];
    }

    /// <summary>
    /// Performs the full platform and version selection flow.
    /// </summary>
    public static TargetSelection AskTarget(string prompt = "Select the target format/platform")
    {
        TargetPlatform platform = AskPlatform(prompt);
        int? version = AskVersion(platform);

        TargetSelection selection = new()
        {
            Platform = platform,
            Version = version
        };

        Console.ForegroundColor = ConsoleColor.Cyan;
        if (selection.IsJdi)
        {
            Console.WriteLine($"Selected: JDI (intermediate format)");
        }
        else
        {
            string versionStr = version.HasValue ? $"JD{version}" : "versionless";
            Console.WriteLine($"Selected: {platform} / {versionStr} ({selection.FormatName} engine)");
        }
        Console.ResetColor();

        return selection;
    }
}