using JustDanceEditor.Converter.Services;
using JustDanceEditor.Formats.JDI;

using System.Runtime.CompilerServices;

namespace JustDanceEditor.Converter.Formats;

internal static class JdiFormatBootstrapper
{
#pragma warning disable CA2255 // Module initializers are intentional here to auto-register formats.
    [ModuleInitializer]
    public static void Initialize()
    {
        JdiFormatRegistry.RegisterFormat(new UbiArtJdiFormat(new RequestValidator(), new SongDataLoader()));
        JdiFormatRegistry.RegisterFormat(new UnityJdiFormat(new RequestValidator()));
        JdiFormatRegistry.RegisterFormat(new JdiPackageFormat());
    }
#pragma warning restore CA2255
}
