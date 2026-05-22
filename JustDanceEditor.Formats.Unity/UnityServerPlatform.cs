namespace JustDanceEditor.Formats.Unity;

public sealed record UnityServerPlatform(string Code, string FolderName, uint BuildTarget);

public static class UnityServerPlatforms
{
    public static readonly UnityServerPlatform PC = new("pc", "PC", 5);
    public static readonly UnityServerPlatform NX = new("nx", "NX", 38);
    public static readonly UnityServerPlatform PS5 = new("ps5", "PS5", 44);
    public static readonly UnityServerPlatform XboxScarlett = new("xbox-scarlett", "XboxScarlett", 42);

    public static IReadOnlyList<UnityServerPlatform> ImportSourcePriority { get; } =
    [
        NX,
        PC,
        PS5,
        XboxScarlett
    ];

    public static IReadOnlyList<UnityServerPlatform> CustomServerDefaults { get; } =
    [
        PC,
        NX,
        PS5,
        XboxScarlett
    ];
}
