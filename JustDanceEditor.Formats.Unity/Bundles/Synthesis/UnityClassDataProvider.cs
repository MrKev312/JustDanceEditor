namespace JustDanceEditor.Formats.Unity.Bundles.Synthesis;

internal static class UnityClassDataProvider
{
    private const string ClassDataResourceName = "JustDanceEditor.Formats.Unity.Assets.classdata.tpk";

    public static Stream OpenClassPackageStream()
    {
        Stream? stream = typeof(UnityClassDataProvider).Assembly.GetManifestResourceStream(ClassDataResourceName);
        if (stream == null)
            throw new FileNotFoundException($"Embedded Unity class database resource '{ClassDataResourceName}' was not found.");

        return stream;
    }
}
