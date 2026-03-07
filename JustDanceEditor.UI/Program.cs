using JustDanceEditor.Audio;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDNextPC;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.Unity.Services;
using JustDanceEditor.UI.Converting;
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
        builder.Services.AddSingleton<ITempFolderManager, SystemTempFolderManager>();

        builder.Services.AddSingleton<ITextureService, DefaultTextureService>();
        builder.Services.AddSingleton<IMediaProcessor, UbiArtMediaProcessor>();
        builder.Services.AddSingleton<ISongDataLoader, SongDataLoader>();
        builder.Services.AddSingleton<IUbiArtEngineDetector, UbiArtEngineDetector>();
        builder.Services.AddSingleton<IAudioConverter, RakiAudioConverter>();

        // Unity services
        builder.Services.AddSingleton<IUnityAssetMaterializer, UnityAssetMaterializerService>();
        // UbiArt asset writer - register directly as both IUbiArtAssetWriter and concrete class
        builder.Services.AddSingleton<UbiArtAssetWriter>();
        builder.Services.AddSingleton<IUbiArtAssetWriter>(sp => sp.GetRequiredService<UbiArtAssetWriter>());

        // Conversion workflow service
        builder.Services.AddSingleton<IConversionWorkflow, ConversionWorkflow>();

        // Factories
        builder.Services.AddSingleton<Func<UbiArtConversionRequest, UbiArtVersionProfile, LayeredFileSystem>>(sp => (req, profile) => new LayeredFileSystem(req, profile, sp.GetRequiredService<ILogger<LayeredFileSystem>>(), sp.GetRequiredService<SystemFileSystem>(), sp.GetRequiredService<ITempFolderManager>()));
        builder.Services.AddSingleton(sp => new Func<string, IntermediateSongPackage>(path => Formats.Unity.Builders.UnityServerIntermediateBuilder.FromServerExport(path, sp.GetRequiredService<ILoggerFactory>().CreateLogger("JustDanceEditor.Formats.Unity.Builders.UnityServerIntermediateBuilder"))));

        // Register IJdiFormat implementations as keyed services
        builder.Services.AddKeyedSingleton<IJdiFormat, JDNextPCJdiFormat>("JDNext PC");
        builder.Services.AddKeyedSingleton<IJdiFormat, UbiArtJdiFormat>("UbiArt");
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