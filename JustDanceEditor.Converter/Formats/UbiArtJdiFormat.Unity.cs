using JustDanceEditor.Converter.Core;
using JustDanceEditor.Formats.Unity;

namespace JustDanceEditor.Converter.Formats;

internal sealed partial class UbiArtJdiFormat
{
    private static void InitializeUnityData(ConversionContext context)
    {
        context.UnityData = UnityExportDataBuilder.Create(context.IntermediatePackage);
    }
}
