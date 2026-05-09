using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.Unity.Services;

using Microsoft.Extensions.DependencyInjection;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnityConverterPlugin : IConverterPlugin
{
    public string Code => "unity";
    public string DisplayName => "Unity";
    public int Priority => 40;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUnityAssetMaterializer, UnityAssetMaterializerService>();
        services.AddSingleton(sp => new Func<string, IntermediateSongPackage>(path => Builders.UnityServerIntermediateBuilder.FromServerExport(
            path,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("JustDanceEditor.Formats.Unity.Builders.UnityServerIntermediateBuilder"))));

        services.AddSingleton<IJdiFormat, UnityJdiFormat>();
        services.AddSingleton<IFormatConversionStrategy, UnityConversionStrategy>();
    }
}
