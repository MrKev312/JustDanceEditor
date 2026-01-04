using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Formats.Unity.Services;
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
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");

        // Register custom texture formats for ImageSharp (once)
        global::TextureConverter.Formats.ImageSharpConfiguration.RegisterCustomFormats();

        // Register services
        builder.Services.AddSingleton<IFileSystem, DefaultFileSystem>();
        builder.Services.AddSingleton<ITextureService, DefaultTextureService>();
        builder.Services.AddSingleton<IMediaProcessor, UbiArtMediaProcessor>();
        builder.Services.AddSingleton<ISongDataLoader, SongDataLoader>();
        builder.Services.AddSingleton<IAudioConverter, Formats.UbiArt.Audio.VGMStreamAdapter>();

        // Unity services
        builder.Services.AddSingleton<IUnityAssetMaterializer, UnityAssetMaterializerService>();

        // Factories
        builder.Services.AddSingleton<Func<UbiArtConversionRequest, Formats.UbiArt.Files.FileSystem>>(sp => req => new Formats.UbiArt.Files.FileSystem(req, sp.GetRequiredService<ILogger<Formats.UbiArt.Files.FileSystem>>()));
        builder.Services.AddSingleton(sp => new Func<string, IntermediateSongPackage>(path => Formats.Unity.Builders.UnityServerIntermediateBuilder.FromServerExport(path, sp.GetRequiredService<ILoggerFactory>().CreateLogger("JustDanceEditor.Formats.Unity.Builders.UnityServerIntermediateBuilder"))));

        // Register IJdiFormat implementations as keyed services
        builder.Services.AddKeyedSingleton<IJdiFormat, Formats.UbiArt.UbiArtJdiFormat>("UbiArt");
        builder.Services.AddKeyedSingleton<IJdiFormat, Formats.Unity.UnityJdiFormat>("Unity");
        builder.Services.AddSingleton<IJdiFormat, JdiFormat>();

        // Register ConsoleApp
        builder.Services.AddSingleton<ConsoleApp>();

        IHost host = builder.Build();

        // Run the console app
        ConsoleApp app = host.Services.GetRequiredService<ConsoleApp>();
        app.Run();

        // If there are async shutdown tasks in the future, host.DisposeAsync can be awaited here
        await Task.CompletedTask;
    }
}