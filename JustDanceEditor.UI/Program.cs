using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.Unity.Services;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.UI.DependencyInjection;
using JustDanceEditor.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JustDanceEditor.UI;

internal class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss "; });

        // Register services
        builder.Services.AddSingleton<IFileSystem, DefaultFileSystem>();
        builder.Services.AddSingleton<ITextureService, DefaultTextureService>();
        builder.Services.AddSingleton<IMediaProcessor, UbiArtMediaProcessor>();
        builder.Services.AddSingleton<ISongDataLoader, SongDataLoader>();
        builder.Services.AddSingleton<JustDanceEditor.Formats.JDI.Services.IAudioConverter, JustDanceEditor.Formats.UbiArt.Audio.VGMStreamAdapter>();

        // Unity services
        builder.Services.AddSingleton<IUnityAssetMaterializer, UnityAssetMaterializerService>();

        // Factories
        builder.Services.AddSingleton<Func<UbiArtConversionRequest, JustDanceEditor.Formats.UbiArt.Files.FileSystem>>(sp => req => new JustDanceEditor.Formats.UbiArt.Files.FileSystem(req, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JustDanceEditor.Formats.UbiArt.Files.FileSystem>>()));
        builder.Services.AddSingleton<Func<string, JustDanceEditor.Formats.JDI.IntermediateSongPackage>>(sp =>
        {
            return new Func<string, JustDanceEditor.Formats.JDI.IntermediateSongPackage>(path => JustDanceEditor.Formats.Unity.Builders.UnityServerIntermediateBuilder.FromServerExport(path, sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("JustDanceEditor.Formats.Unity.Builders.UnityServerIntermediateBuilder")));
        });

        // Register IJdiFormat implementations as keyed services
        builder.Services.AddKeyedSingleton<IJdiFormat, JustDanceEditor.Formats.UbiArt.UbiArtJdiFormat>("UbiArt");
        builder.Services.AddKeyedSingleton<IJdiFormat, JustDanceEditor.Formats.Unity.UnityJdiFormat>("Unity");
        builder.Services.AddSingleton<IJdiFormat, JustDanceEditor.Formats.JDI.JdiFormat>();

        // Register ConsoleApp
        builder.Services.AddSingleton<ConsoleApp>();

        var host = builder.Build();

        // Run the console app
        var app = host.Services.GetRequiredService<ConsoleApp>();
        app.Run();

        // If there are async shutdown tasks in the future, host.DisposeAsync can be awaited here
        await Task.CompletedTask;
    }
}