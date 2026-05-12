using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.Audio.NAudio;
using KevInc.Texture.ImageSharp;
using KevInc.Texture.Nintendo.ImageSharp;
using KevInc.Texture.Xbox.ImageSharp;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtConverterPlugin : IConverterPlugin
{
    public string Code => "ubiart";
    public string DisplayName => "UbiArt";
    public int Priority => 10;

    public void ConfigureServices(IServiceCollection services)
    {
        TextureImageSharpConfiguration.RegisterDdsFormat();
        NintendoImageSharpConfiguration.RegisterTextureFormats();
        Xbox360ImageSharpConfiguration.RegisterTextureFormat();
        UbiArtTextureImageSharpConfiguration.RegisterTextureFormat();

        services.TryAddSingleton<IFileSystem, SystemFileSystem>();
        services.TryAddSingleton<SystemFileSystem>();
        services.TryAddSingleton<ITempFolderManager, SystemTempFolderManager>();
        services.TryAddSingleton<ITextureService, DefaultTextureService>();
        services.TryAddSingleton<IMediaProcessor, DefaultMediaProcessor>();
        services.TryAddSingleton<IAudioConverter, RakiAudioConverter>();

        services.AddSingleton<ISongDataLoader, SongDataLoader>();
        services.AddSingleton<IUbiArtEngineDetector, UbiArtEngineDetector>();
        services.AddSingleton<UbiArtAssetWriter>();
        services.AddSingleton<IUbiArtAssetWriter>(sp => sp.GetRequiredService<UbiArtAssetWriter>());
        services.AddSingleton<Func<UbiArtConversionRequest, UbiArtVersionProfile, JustDanceUbiArtFileSystem>>(sp => (req, profile) => new JustDanceUbiArtFileSystem(
            req,
            profile,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JustDanceUbiArtFileSystem>>(),
            sp.GetRequiredService<SystemFileSystem>(),
            sp.GetRequiredService<ITempFolderManager>()));

        services.AddSingleton<IJdiFormat, UbiArtJdiFormat>();
        services.AddSingleton<IFormatConversionStrategy, UbiArtConversionStrategy>();
        services.AddSingleton<ISongPreviewProvider, UbiArtSongPreviewProvider>();
    }
}
