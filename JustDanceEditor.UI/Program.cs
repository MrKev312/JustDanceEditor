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
        TextureConverter.Formats.ImageSharpConfiguration.RegisterCustomFormats();

        // Register services
        builder.Services.AddSingleton<IFileSystem, SystemFileSystem>();
        // Register system implementations used by non-UI projects
        builder.Services.AddSingleton<SystemFileSystem>();
        builder.Services.AddSingleton<Formats.UbiArt.Files.ITempFolderManager, Formats.UbiArt.Files.SystemTempFolderManager>();

        builder.Services.AddSingleton<ITextureService, DefaultTextureService>();
        builder.Services.AddSingleton<IMediaProcessor, UbiArtMediaProcessor>();
        builder.Services.AddSingleton<ISongDataLoader, SongDataLoader>();
        builder.Services.AddSingleton<IUbiArtEngineDetector, UbiArtEngineDetector>();
        builder.Services.AddSingleton<IAudioConverter, Formats.UbiArt.Audio.VGMStreamAdapter>();

        // Unity services
        builder.Services.AddSingleton<IUnityAssetMaterializer, UnityAssetMaterializerService>();
        // UbiArt asset writer service (wrapper to allow DI/testing)
        builder.Services.AddSingleton<JustDanceEditor.Formats.UbiArt.Services.IUbiArtAssetWriter, JustDanceEditor.Formats.UbiArt.Services.UbiArtAssetWriterService>();

        // Factories
        builder.Services.AddSingleton<Func<UbiArtConversionRequest, UbiArtVersionProfile, Formats.UbiArt.Files.LayeredFileSystem>>(sp => (req, profile) => new Formats.UbiArt.Files.LayeredFileSystem(req, profile, sp.GetRequiredService<ILogger<Formats.UbiArt.Files.LayeredFileSystem>>(), sp.GetRequiredService<SystemFileSystem>(), sp.GetRequiredService<Formats.UbiArt.Files.ITempFolderManager>()));
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