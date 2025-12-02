using JustDanceEditor.Converter.Core;

namespace JustDanceEditor.Converter.Unity;

internal static class UnityDataExtensions
{
    public static UnityExportData RequireUnityData(this ConversionContext context)
    {
        if (context.UnityData == null)
            throw new InvalidOperationException("Unity export data has not been initialized for this conversion context.");
        return context.UnityData;
    }
}
