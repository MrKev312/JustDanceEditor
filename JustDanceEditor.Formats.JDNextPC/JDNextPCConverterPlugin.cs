using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JustDanceEditor.Formats.JDNextPC;

public sealed class JDNextPCConverterPlugin : IConverterPlugin
{
    public string Code => "jdnext-pc";
    public string DisplayName => "JDNext PC";
    public int Priority => 30;

    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IFileSystem, SystemFileSystem>();
        services.TryAddSingleton<IMediaProcessor, DefaultMediaProcessor>();
        services.TryAddSingleton<ITextureService, DefaultTextureService>();

        services.AddSingleton<IJdiFormat, JDNextPCJdiFormat>();
        services.AddSingleton<IFormatConversionStrategy, JDNextPCConversionStrategy>();
    }
}