using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Export;
using JustDanceEditor.Formats.UbiArt.Services.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Globalization;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.UbiArt.Services;

public sealed partial class UbiArtAssetWriter(ILogger<UbiArtAssetWriter> logger, IUbiArtExporterFactory? factory = null, ITextureProcessor? textureProcessor = null) : IUbiArtAssetWriter
{
    private readonly IUbiArtExporterFactory _factory = factory ?? new UbiArtExporterFactory();
    private readonly ITextureProcessor _textureProcessor = textureProcessor ?? new TextureProcessor();

    public async Task ExportAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IUbiArtLayout? layout = null, IFileSystem? io = null)
    {
        layout ??= new UbiArtLayoutResolver();
        IFileSystem iofs = io ?? new SystemFileSystem();

        IPlatformExporter platformExporter = _factory.GetPlatformExporter(platform);

        // Get the appropriate engine content generator based on platform and version
        IEngineContentGenerator engineGenerator = _factory.GetEngineContentGenerator(engineVersion, platform);

        ExportContext exportContext = new(outputFolder, layout, iofs);

        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string platformRoot = platformExporter.GetPlatformRootFolder(mapNameLower);

        string mapWorldRelative = layout.GetMapWorldFolder("", mapName, platform, engineVersion);
        string mapWorldBase = Path.Combine(platformRoot, mapWorldRelative);
        string rawMapWorldBase = layout.GetMapWorldFolder("", mapName, UbiArtPlatform.Uncooked, engineVersion);

        logger.LogInformation("Exporting {MapName} ({Platform}, {Version})...", mapName, platform, engineVersion);

        // 1. Generate Colors from Background
        if (!string.IsNullOrEmpty(materializedRoot))
        {
            await GenerateColorsAsync(package, materializedRoot, iofs);
        }

        // 2. Process Audio
        await PrepareAndWriteAudioAsync(package, materializedRoot, mapWorldBase, exportContext, platformExporter, engineGenerator);

        // 3. Generate & Write Content
        await WriteEngineContentAsync(package, mapWorldBase, exportContext, platformExporter, engineGenerator, layout, platform, engineVersion);

        // 4. Process & Write Textures (Using TextureProcessor for resizing/compositing)
        if (!string.IsNullOrEmpty(materializedRoot))
        {
            await ProcessAndWriteTexturesAsync(package, materializedRoot, mapWorldBase, exportContext, platformExporter, engineGenerator, layout, platform, engineVersion);

            // 5. Process Raw Assets (Video & Moves)
            await ProcessRawAssetsAsync(package, materializedRoot, rawMapWorldBase, exportContext, platform, engineVersion, layout);
        }

        logger.LogInformation("Export completed.");
    }

    private async Task WriteEngineContentAsync(
        IntermediateSongPackage package,
        string mapWorldBase,
        ExportContext ctx,
        IPlatformExporter exporter,
        IEngineContentGenerator generator,
        IUbiArtLayout layout,
        UbiArtPlatform platform,
        UbiArtEngineVersion version)
    {
        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string platformRoot = exporter.GetPlatformRootFolder(mapNameLower);

        string audioFolder = Path.Combine(platformRoot, layout.GetAudioFolder("", mapName, platform, version));
        string timelineFolder = Path.Combine(platformRoot, layout.GetTimelineFolder("", mapName, platform, version));
        string cinematicsFolder = Path.Combine(mapWorldBase, "cinematics");
        string menuartFolder = Path.Combine(mapWorldBase, "menuart");
        string autodanceFolder = Path.Combine(mapWorldBase, "autodance");
        string videosFolder = Path.Combine(mapWorldBase, "videoscoach");
        string graphFolder = Path.Combine(mapWorldBase, "graph");

        // Check if using legacy format (Wii 2016-2020)
        bool isLegacyFormat = generator is LegacyEngineContentGenerator;

        // SongDesc
        if (isLegacyFormat)
        {
            // Legacy format: SongDesc goes to cache/legacyconverteddata/{mapname}/songdesc.main_legacy.tpl.ckd
            string legacyFolder = Path.Combine(platformRoot, "cache", "legacyconverteddata", mapNameLower);
            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(legacyFolder, "songdesc.main_legacy.tpl"), generator.GenerateSongDesc(package));
            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"cache/legacyconverteddata/{mapNameLower}/songdesc.main_legacy.tpl"));
        }
        else
        {
            await exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, "songdesc.tpl"), generator.GenerateSongDesc(package));
            await exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"world/maps/{mapNameLower}/songdesc.tpl"));
        }

        // MusicTrack
        if (platform != UbiArtPlatform.Uncooked)
        {
            if (isLegacyFormat)
            {
                // Legacy format: MusicTrack goes to cache/legacyconverteddata/{mapname}/audio/{mapname}_musictrack.main_legacy.tpl.ckd
                string legacyAudioFolder = Path.Combine(platformRoot, "cache", "legacyconverteddata", mapNameLower, "audio");
                await exporter.WriteBinaryFileAsync(ctx, Path.Combine(legacyAudioFolder, $"{mapNameLower}_musictrack.main_legacy.tpl"), generator.GenerateMusicTrack(package));
            }
            else
            {
                await exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl"), generator.GenerateMusicTrack(package));
            }

            await exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_sequence.tpl"), generator.GenerateSequenceTpl());
            await exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}.stape"), generator.GenerateSoundTape(mapName));
            if (package.TimelineStructure.StartBeat < 0)
            {
                await exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, "amb", $"amb_{mapNameLower}_intro.tpl"), generator.GenerateAmbTpl(mapName));
            }
        }

        // Tapes
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.dtape"), generator.GenerateDanceTape(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.ktape"), generator.GenerateKaraokeTape(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tape"), generator.GenerateMainSequenceTape(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.tpl"), generator.GenerateAutodanceTape(package));

        // Actors
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.act"), generator.GenerateGenericActor("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl"));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.tpl"), generator.GenerateTapeCaseTpl(mapName, "dance"));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.act"), generator.GenerateGenericActor("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl"));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.tpl"), generator.GenerateTapeCaseTpl(mapName, "karaoke"));

        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.act"), generator.GenerateGenericActor("MasterTape", $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl"));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tpl"), generator.GenerateMainSequenceTpl(mapName));

        // Scenes
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.isc"), generator.GenerateMainScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_audio.isc"), generator.GenerateAudioScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml.isc"), generator.GenerateTimelineScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_cine.isc"), generator.GenerateCinematicsScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(menuartFolder, $"{mapNameLower}_menuart.isc"), generator.GenerateMenuArtScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.isc"), generator.GenerateAutodanceScene(package));
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(graphFolder, $"{mapNameLower}_graph.isc"), generator.GenerateGraphScene(mapName));

        // Binary/Special
        await exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.sgs"), generator.GenerateSgs());
        if (platform != UbiArtPlatform.Uncooked)
        {
            await exporter.WriteEngineResourceAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video.isc"), generator.GenerateVideoScene(mapName));
            // Only Modern engines typically use map preview video scenes, but for 2015/Wii support we assume standard structure if generator supports it
            if (version > UbiArtEngineVersion.JD2015) // Not strictly necessary for Wii but keeps symmetry
                await exporter.WriteEngineResourceAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video_map_preview.isc"), generator.GenerateVideoMapPreviewScene(mapName));

            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, "video_player_main.act"), generator.GenerateVideoPlayerActor(mapName, false));
            // Only write preview actor if modern or if generator provides it (JD2015 generator might return empty or logic check)
            if (version > UbiArtEngineVersion.JD2015)
                await exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, "video_player_map_preview.act"), generator.GenerateVideoPlayerActor(mapName, true));

            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}.mpd"), generator.GenerateMpd());
            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.act"), generator.GenerateAutodanceActor(mapName));
        }
    }

    private async Task PrepareAndWriteAudioAsync(IntermediateSongPackage package, string? materializedRoot, string mapWorldBase, ExportContext ctx, IPlatformExporter exporter, IEngineContentGenerator generator)
    {
        if (string.IsNullOrEmpty(materializedRoot))
            return;
        string sourceFile = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!ctx.IO.FileExists(sourceFile))
            return;

        string platformRoot = exporter.GetPlatformRootFolder(package.Metadata.MapName.ToLowerInvariant());
        string audioFolderRel = Path.Combine(platformRoot, "world", "maps", package.Metadata.MapName.ToLowerInvariant(), "audio");
        string ambFolderRel = Path.Combine(audioFolderRel, "amb");
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

        string tempWav = ctx.IO.Combine(ctx.IO.GetTempPath(), $"jdi_{Guid.NewGuid()}.wav");
        try
        {
            // First convert to WAV because encoders usually expect WAV input
            IConversion conv = FFmpeg.Conversions.New();
            conv.SetOverwriteOutput(true);
            conv.AddParameter($"-i \"{sourceFile}\" -ar 48000 -ac 2");
            conv.SetOutput(tempWav);
            await conv.Start();

            double startBeat = package.TimelineStructure.StartBeat;
            double cutSeconds = 0;
            if (startBeat < 0 && package.TimelineStructure.Markers.Count > 1)
            {
                cutSeconds = package.TimelineStructure.Markers[(int)Math.Abs(startBeat)] / 48000.0;
            }

            // Create Amb (Intro) Audio if needed
            if (cutSeconds > 0.001)
            {
                string ambTemp = ctx.IO.Combine(ctx.IO.GetTempPath(), $"amb_{Guid.NewGuid()}.wav");
                IConversion ambConv = FFmpeg.Conversions.New();
                ambConv.SetOverwriteOutput(true);
                ambConv.AddParameter($"-i \"{tempWav}\" -t {cutSeconds.ToString(CultureInfo.InvariantCulture)}");
                ambConv.SetOutput(ambTemp);
                await ambConv.Start();

                await exporter.WriteAudioAsync(ctx, Path.Combine(ambFolderRel, $"amb_{mapNameLower}_intro.wav"), ambTemp);
                ctx.IO.DeleteFile(ambTemp);
            }

            // Create Main Audio (cut after intro)
            string mainTemp = ctx.IO.Combine(ctx.IO.GetTempPath(), $"main_{Guid.NewGuid()}.wav");
            IConversion mainConv = FFmpeg.Conversions.New();
            mainConv.SetOverwriteOutput(true);
            mainConv.AddParameter($"-ss {cutSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{tempWav}\"");
            mainConv.SetOutput(mainTemp);
            await mainConv.Start();

            await exporter.WriteAudioAsync(ctx, Path.Combine(audioFolderRel, $"{mapNameLower}.wav"), mainTemp, package.TimelineStructure.Markers);
            ctx.IO.DeleteFile(mainTemp);
        }
        finally
        {
            if (ctx.IO.FileExists(tempWav))
                ctx.IO.DeleteFile(tempWav);
        }
    }

    private async Task ProcessAndWriteTexturesAsync(
        IntermediateSongPackage package,
        string materializedRoot,
        string mapWorldBase,
        ExportContext ctx,
        IPlatformExporter exporter,
        IEngineContentGenerator generator,
        IUbiArtLayout layout,
        UbiArtPlatform platform,
        UbiArtEngineVersion version)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        string platformRoot = exporter.GetPlatformRootFolder(mapNameLower);
        string pictosRel = Path.Combine(platformRoot, layout.GetPictosFolder("", package.Metadata.MapName, platform, version));
        string menuRel = Path.Combine(mapWorldBase, "menuart", "textures");
        string actorsRel = Path.Combine(mapWorldBase, "menuart", "actors");

        // 1. Pictograms
        string pictosSource = ctx.IO.Combine(materializedRoot, "assets", "pictograms");
        if (ctx.IO.DirectoryExists(pictosSource))
        {
            foreach (string file in ctx.IO.GetFiles(pictosSource))
            {
                using Image<Bgra32> img = Image.Load<Bgra32>(file);
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                await exporter.WriteTextureAsync(ctx, Path.Combine(pictosRel, $"{name}.png"), img);
            }
        }

        // 2. MenuArt
        IEnumerable<ProcessedTexture> processedTextures = _textureProcessor.ProcessAssets(package, materializedRoot, ctx.IO);

        foreach (ProcessedTexture texture in processedTextures)
        {
            // Write texture
            await exporter.WriteTextureAsync(ctx, Path.Combine(menuRel, $"{texture.Name}.tga"), texture.Image);

            // Write actor (only for Cooked)
            if (platform != UbiArtPlatform.Uncooked)
            {
                await exporter.WriteBinaryFileAsync(ctx, Path.Combine(actorsRel, $"{texture.Name}.act"), generator.GenerateMenuArtActor(texture.Name, package.Metadata.MapName));
            }

            texture.Image.Dispose();
        }
    }

    private async Task GenerateColorsAsync(IntermediateSongPackage package, string materializedRoot, IFileSystem io)
    {
        if (package.Metadata.AdditionalMetadata.ContainsKey("songcolor_1a"))
            return;

        string assetsDir = io.Combine(materializedRoot, "assets", "coaches");
        string? bkgFile = io.GetFiles(assetsDir)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("coachesbackground", StringComparison.OrdinalIgnoreCase));

        if (bkgFile == null)
            return;

        try
        {
            using Image<Bgra32> img = await Image.LoadAsync<Bgra32>(bkgFile);
            ColorThemeGenerator.SongTheme theme = ColorThemeGenerator.GenerateFromImage(img);

            package.Metadata.AdditionalMetadata["songcolor_1a"] = theme.Color1A.ToHex();
            package.Metadata.AdditionalMetadata["songcolor_1b"] = theme.Color1B.ToHex();
            package.Metadata.AdditionalMetadata["songcolor_2a"] = theme.Color2A.ToHex();
            package.Metadata.AdditionalMetadata["songcolor_2b"] = theme.Color2B.ToHex();

            logger.LogInformation("Generated song colors: 1A={Color1A}, 1B={Color1B}, 2A={Color2A}, 2B={Color2B}",
                theme.Color1A.ToHex(), theme.Color1B.ToHex(), theme.Color2A.ToHex(), theme.Color2B.ToHex());
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to generate song colors from background: {Message}", ex.Message);
        }
    }

    private async Task ProcessRawAssetsAsync(
        IntermediateSongPackage package,
        string materializedRoot,
        string rawMapWorldBase,
        ExportContext ctx,
        UbiArtPlatform platform,
        UbiArtEngineVersion version,
        IUbiArtLayout layout)
    {
        string videoSourceDir = ctx.IO.Combine(materializedRoot, "assets", "video");

        if (ctx.IO.DirectoryExists(videoSourceDir))
        {
            string? sourceFile;

            // Wii needs special handling
            if (platform == UbiArtPlatform.Wii)
            {
                // Use specific 2-pass encoding for Wii to meet strict requirements
                sourceFile = await JDI.Video.JdiVideoConverter.EnsureWiiVideoAsync(materializedRoot, logger);
            }
            else if (version == UbiArtEngineVersion.JD2017)
            {
                sourceFile = await JDI.Video.JdiVideoConverter.EnsureVideoFormatAsync(
                    materializedRoot,
                    ".webm",
                    "vp8",
                    logger
                );
            }
            else
            {
                sourceFile = ctx.IO.GetFiles(videoSourceDir, "*.webm")
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            }

            // Fallback if specific conversion failed or wasn't needed
            if (sourceFile == null)
            {
                sourceFile = ctx.IO.GetFiles(videoSourceDir, "*.webm")
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            }

            if (sourceFile != null && ctx.IO.FileExists(sourceFile))
            {
                string relFolder = platform == UbiArtPlatform.Uncooked
                    ? layout.GetMediaFolder("", package.Metadata.MapName, platform, version)
                    : Path.Combine(rawMapWorldBase, "videoscoach");

                string destFileName = $"{package.Metadata.MapName.ToLowerInvariant()}.webm";

                if (platform == UbiArtPlatform.NX)
                {
                    destFileName = version == UbiArtEngineVersion.JD2017
                        ? $"{package.Metadata.MapName.ToLowerInvariant()}.webm"
                        : $"{package.Metadata.MapName.ToLowerInvariant()}.vp9.720.webm";
                }
                if (platform == UbiArtPlatform.Wii)
                {
                    destFileName = $"{package.Metadata.MapName.ToLowerInvariant()}.wii.webm";
                }

                string destPath = Path.Combine(relFolder, destFileName);
                string fullDest = ctx.IO.Combine(ctx.OutputFolder, destPath);

                ctx.IO.CreateDirectory(Path.GetDirectoryName(fullDest)!);
                ctx.IO.Copy(sourceFile, fullDest, true);

                logger.LogInformation("Video exported to {Path} (Source: {Source})", destFileName, Path.GetFileName(sourceFile));
            }
        }

        string movesSource = ctx.IO.Combine(materializedRoot, "assets", "moves");
        if (ctx.IO.DirectoryExists(movesSource))
        {
            // Moves are platform specific (Wii/WiiU uses .msm, NX uses .msc usually but .msm often compatible)
            // For Wii, we need to ensure they are put in the right folder.
            string movesFolder = Path.Combine(rawMapWorldBase, "timeline", "moves", "wiiu"); // "wiiu" is often the folder name even on wii/nx
            if (platform == UbiArtPlatform.Wii)
                movesFolder = Path.Combine(rawMapWorldBase, "timeline", "moves", "wii");

            ctx.IO.CreateDirectory(ctx.IO.Combine(ctx.OutputFolder, movesFolder));

            foreach (string moveFile in ctx.IO.GetFiles(movesSource, "*.msm"))
            {
                string fileName = Path.GetFileName(moveFile).ToLowerInvariant();
                string destPath = Path.Combine(movesFolder, fileName);
                string fullDest = ctx.IO.Combine(ctx.OutputFolder, destPath);

                ctx.IO.Copy(moveFile, fullDest, true);
            }
        }
    }
}