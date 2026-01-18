using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Export;
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
        IEngineContentGenerator engineGenerator = _factory.GetEngineContentGenerator(engineVersion);
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

        // SongDesc
        await exporter.WriteTextFileAsync(ctx, Path.Combine(mapWorldBase, "songdesc.tpl"), generator.GenerateSongDesc(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"world/maps/{mapNameLower}/songdesc.tpl"));

        // MusicTrack
        if (platform != UbiArtPlatform.Uncooked)
        {
            await exporter.WriteTextFileAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl"), generator.GenerateMusicTrack(package));
            await exporter.WriteTextFileAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_sequence.tpl"), generator.GenerateSequenceTpl());
            await exporter.WriteTextFileAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}.stape"), generator.GenerateSoundTape(mapName));
            if (package.TimelineStructure.StartBeat < 0)
            {
                await exporter.WriteTextFileAsync(ctx, Path.Combine(audioFolder, "amb", $"amb_{mapNameLower}_intro.tpl"), generator.GenerateAmbTpl(mapName));
            }
        }

        // Tapes
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.dtape"), generator.GenerateDanceTape(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.ktape"), generator.GenerateKaraokeTape(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tape"), generator.GenerateMainSequenceTape(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.tpl"), generator.GenerateAutodanceTape(package));

        // Actors
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.act"), generator.GenerateGenericActor("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_dance.tpl"));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.tpl"), generator.GenerateTapeCaseTpl(mapName, "dance"));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.act"), generator.GenerateGenericActor("TapeCase_Template", $"world/maps/{mapNameLower}/timeline/{mapNameLower}_tml_karaoke.tpl"));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.tpl"), generator.GenerateTapeCaseTpl(mapName, "karaoke"));

        await exporter.WriteTextFileAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.act"), generator.GenerateGenericActor("MasterTape", $"world/maps/{mapNameLower}/cinematics/{mapNameLower}_mainsequence.tpl"));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tpl"), generator.GenerateMainSequenceTpl(mapName));

        // Scenes
        await exporter.WriteTextFileAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.isc"), generator.GenerateMainScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_audio.isc"), generator.GenerateAudioScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml.isc"), generator.GenerateTimelineScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_cine.isc"), generator.GenerateCinematicsScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(menuartFolder, $"{mapNameLower}_menuart.isc"), generator.GenerateMenuArtScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.isc"), generator.GenerateAutodanceScene(package));
        await exporter.WriteTextFileAsync(ctx, Path.Combine(graphFolder, $"{mapNameLower}_graph.isc"), generator.GenerateGraphScene());

        // Binary/Special
        await exporter.WriteTextFileAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.sgs"), generator.GenerateSgs());
        if (platform != UbiArtPlatform.Uncooked)
        {
            await exporter.WriteTextFileAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video.isc"), generator.GenerateVideoScene(mapName));
            await exporter.WriteTextFileAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video_map_preview.isc"), generator.GenerateVideoMapPreviewScene(mapName));

            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, "video_player_main.act"), generator.GenerateVideoPlayerActor(mapName, false));
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

        // 1. Pictograms (Still direct copy as TextureProcessor is mainly for MenuArt currently)
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

        // 2. MenuArt (Uses TextureProcessor)
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
        // Don't overwrite if manually set
        if (package.Metadata.AdditionalMetadata.ContainsKey("songcolor_1a"))
            return;

        string assetsDir = io.Combine(materializedRoot, "assets", "coaches");

        // Look for coachesbackground (preferred) or just the cover
        string? bkgFile = io.GetFiles(assetsDir)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("coachesbackground", StringComparison.OrdinalIgnoreCase));

        if (bkgFile == null)
            return;

        try
        {
            // Load image
            using var img = await Image.LoadAsync<Bgra32>(bkgFile); // Or use io.OpenRead logic if strictly required

            // Generate Theme (Returns normal Colors)
            var theme = ColorThemeGenerator.GenerateFromImage(img);

            // Convert to UbiArt Array format and store in metadata
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

            // JD2017 Logic: Requires VP8 (libvpx) in a WebM container
            if (version == UbiArtEngineVersion.JD2017)
            {
                // Use JdiVideoConverter to Ensure VP8 format (utilizing scratch cache)
                // We pass "libvpx" for video
                sourceFile = await JDI.Video.JdiVideoConverter.EnsureVideoFormatAsync(
                    materializedRoot,
                    ".webm",
                    "libvpx",
                    logger
                );

                // If conversion failed, fallback to raw copy (best effort)
                if (sourceFile == null)
                {
                    sourceFile = ctx.IO.GetFiles(videoSourceDir, "*.webm")
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                }
            }
            else
            {
                // JD2018+ / Standard Logic: Just grab the largest webm (usually VP9 or VP8, engine supports both)
                sourceFile = ctx.IO.GetFiles(videoSourceDir, "*.webm")
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            }

            if (sourceFile != null && ctx.IO.FileExists(sourceFile))
            {
                string relFolder = platform == UbiArtPlatform.Uncooked
                    ? layout.GetMediaFolder("", package.Metadata.MapName, platform, version)
                    : Path.Combine(rawMapWorldBase, "videoscoach");

                // Filename logic
                string destFileName = $"{package.Metadata.MapName.ToLowerInvariant()}.webm";

                // On NX, newer engines (2018+) need a .vp9.720.webm naming convention
                // but just .webm for 2017.
                if (platform == UbiArtPlatform.NX)
                {
                    destFileName = version == UbiArtEngineVersion.JD2017
                        ? $"{package.Metadata.MapName.ToLowerInvariant()}.webm"
                        : $"{package.Metadata.MapName.ToLowerInvariant()}.vp9.720.webm";
                }

                string destPath = Path.Combine(relFolder, destFileName);
                string fullDest = ctx.IO.Combine(ctx.OutputFolder, destPath);

                ctx.IO.CreateDirectory(Path.GetDirectoryName(fullDest)!);

                // Use Copy logic. Note: sourceFile might be in scratch (temp) or assets (input).
                ctx.IO.Copy(sourceFile, fullDest, true);

                logger.LogInformation("Video exported to {Path} (Source: {Source})", destFileName, Path.GetFileName(sourceFile));
            }
        }
        
        string movesSource = ctx.IO.Combine(materializedRoot, "assets", "moves");
        if (ctx.IO.DirectoryExists(movesSource))
        {
            string movesFolder = Path.Combine(rawMapWorldBase, "timeline", "moves", "wiiu");
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