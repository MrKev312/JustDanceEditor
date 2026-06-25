using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Globalization;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Export;

public sealed partial class UbiArtAssetWriter(ILogger<UbiArtAssetWriter> logger, IUbiArtExporterFactory? factory = null, IMediaProcessor? mediaProcessor = null) : IUbiArtAssetWriter
{
    private readonly IUbiArtExporterFactory _factory = factory ?? new UbiArtExporterFactory();
    private readonly IMediaProcessor _mediaProcessor = mediaProcessor ?? new DefaultMediaProcessor();

    public async Task ExportAsync(IntermediateSongPackage package, string? materializedRoot, string outputFolder, UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IUbiArtLayout? layout = null, IFileSystem? io = null)
    {
        layout ??= engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => new JD2014LayoutResolver(),
            UbiArtEngineVersion.JD2015 => new JD2015LayoutResolver(),
            _ => new UbiArtLayoutResolver()
        };
        IFileSystem iofs = io ?? new SystemFileSystem();

        IPlatformExporter platformExporter = _factory.GetPlatformExporter(platform);

        // Get the appropriate engine content generator based on platform and version
        IEngineContentGenerator engineGenerator = _factory.GetEngineContentGenerator(engineVersion, platform);

        UbiArtGameFolderIpkExporter? ipkExporter = null;
        string? stagingOutputFolder = null;
        string assetOutputFolder = outputFolder;

        if (platform != UbiArtPlatform.Uncooked &&
            UbiArtGameFolderIpkExporter.TryCreate(outputFolder, platform, logger, out ipkExporter))
        {
            stagingOutputFolder = Path.Combine(Path.GetTempPath(), "jdi_ubiart_export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingOutputFolder);
            assetOutputFolder = stagingOutputFolder;

            logger.LogInformation(
                "Detected UbiArt game folder at '{OutputFolder}'. Staging loose files before IPK repack into '{ArchiveFolder}'.",
                outputFolder,
                ipkExporter!.ArchiveFolder);
        }

        ExportContext exportContext = new(assetOutputFolder, layout, iofs, engineVersion, _mediaProcessor);

        string mapName = package.Metadata.MapName;
        string mapNameLower = mapName.ToLowerInvariant();
        string platformRoot = platformExporter.GetPlatformRootFolder(mapNameLower);

        string mapWorldRelative = layout.GetMapWorldFolder("", mapNameLower, platform, engineVersion);
        string mapWorldBase = Path.Combine(platformRoot, mapWorldRelative);
        string rawMapWorldBase = layout.GetMapWorldFolder("", mapNameLower, platform, engineVersion);

        logger.LogInformation("Exporting {MapName} ({Platform}, {Version})...", mapName, platform, engineVersion);

        // 1. Generate Colors from Background
        if (!string.IsNullOrEmpty(materializedRoot))
        {
            await GenerateColorsAsync(package, materializedRoot, iofs);
        }

        try
        {
            Task audioTask = PrepareAndWriteAudioAsync(package, materializedRoot, mapWorldBase, exportContext, platformExporter, engineGenerator);
            Task contentTask = WriteEngineContentAsync(package, mapWorldBase, exportContext, platformExporter, engineGenerator, layout, platform, engineVersion);

            if (!string.IsNullOrEmpty(materializedRoot))
            {
                Task textureTask = ProcessAndWriteTexturesAsync(package, materializedRoot, mapWorldBase, exportContext, platformExporter, engineGenerator, layout, platform, engineVersion);
                Task rawAssetTask = ProcessRawAssetsAsync(package, materializedRoot, rawMapWorldBase, exportContext, platform, engineVersion, layout);
                await Task.WhenAll(audioTask, contentTask, textureTask, rawAssetTask);
            }
            else
            {
                await Task.WhenAll(audioTask, contentTask);
            }

            if (ipkExporter is not null && stagingOutputFolder is not null)
                await ipkExporter.ApplyAsync(stagingOutputFolder, package, engineVersion);
        }
        finally
        {
            if (stagingOutputFolder is not null && Directory.Exists(stagingOutputFolder))
            {
                try
                {
                    Directory.Delete(stagingOutputFolder, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to delete UbiArt export staging folder '{StagingFolder}'.", stagingOutputFolder);
                }
            }
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
        string mapWorldRelative = layout.GetMapWorldFolder("", mapNameLower, platform, version);

        string audioFolder = Path.Combine(platformRoot, layout.GetAudioFolder("", mapNameLower, platform, version));
        string timelineFolder = Path.Combine(platformRoot, layout.GetTimelineFolder("", mapNameLower, platform, version));
        string cinematicsFolder = Path.Combine(mapWorldBase, "cinematics");
        string menuartFolder = Path.Combine(mapWorldBase, "menuart");
        string autodanceFolder = Path.Combine(mapWorldBase, "autodance");
        string videosFolder = Path.Combine(mapWorldBase, "videoscoach");
        string graphFolder = Path.Combine(mapWorldBase, "graph");

        // Check if using legacy binary format.
        bool isLegacyFormat = generator is LegacyEngineContentGenerator;
        bool useLegacyConvertedData = isLegacyFormat && version >= UbiArtEngineVersion.JD2016;
        bool writeAutodanceResources = ShouldWriteAutodanceResources(platform, isLegacyFormat);
        bool writeSoundSequence = version != UbiArtEngineVersion.JD2014 && ShouldWriteSoundSequence(package);

        List<Task> writeTasks = [];

        if (useLegacyConvertedData)
        {
            // Legacy format: SongDesc goes to cache/legacyconverteddata/{mapname}/songdesc.main_legacy.tpl.ckd
            string legacyFolder = Path.Combine(platformRoot, "cache", "legacyconverteddata", mapNameLower);
            writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(legacyFolder, "songdesc.main_legacy.tpl"), generator.GenerateSongDesc(package)));
            writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"cache/legacyconverteddata/{mapNameLower}/songdesc.main_legacy.tpl")));
        }
        else
        {
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, "songdesc.tpl"), generator.GenerateSongDesc(package)));
            if (version != UbiArtEngineVersion.JD2014)
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"{mapWorldRelative}/songdesc.tpl")));
        }

        if (platform != UbiArtPlatform.Uncooked)
        {
            if (useLegacyConvertedData)
            {
                // Legacy format: MusicTrack goes to cache/legacyconverteddata/{mapname}/audio/{mapname}_musictrack.main_legacy.tpl.ckd
                string legacyAudioFolder = Path.Combine(platformRoot, "cache", "legacyconverteddata", mapNameLower, "audio");
                writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(legacyAudioFolder, $"{mapNameLower}_musictrack.main_legacy.tpl"), generator.GenerateMusicTrack(package)));
            }
            else
            {
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl"), generator.GenerateMusicTrack(package)));
            }

            if (writeSoundSequence)
            {
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_sequence.tpl"), generator.GenerateSequenceTpl()));
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}.stape"), generator.GenerateSoundTape(mapName)));
            }

            if (package.TimelineStructure.StartBeat < 0)
            {
                string ambTemplateName = version == UbiArtEngineVersion.JD2014
                    ? $"set_amb_{mapNameLower}_intro.tpl"
                    : $"amb_{mapNameLower}_intro.tpl";
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, "amb", ambTemplateName), generator.GenerateAmbTpl(mapName)));
            }
        }
        else
        {
            // Uncooked platform needs musictrack.tpl, .trk file, and AMB files
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapName}_musictrack.tpl"), generator.GenerateMusicTrack(package)));

            // Write .trk file with structure data
            writeTasks.Add(WriteUncookedTrkFileAsync(package, ctx, Path.Combine(audioFolder, $"{mapName}.trk")));

            // Write AMB files if there's a negative start beat
            if (package.TimelineStructure.StartBeat < 0)
            {
                string ambFolder = Path.Combine(mapWorldBase, "Audio", "AMB");
                writeTasks.Add(WriteUncookedAmbFilesAsync(package, ctx, ambFolder, mapName));
            }
        }

        if (version == UbiArtEngineVersion.JD2014 && generator is LegacyEngineContentGenerator legacyGenerator)
        {
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, "timeline.tpl"), legacyGenerator.GenerateJd2014Timeline(package)));
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, "timeline.act"), legacyGenerator.GenerateJd2014TimelineActor(mapName)));
        }
        else
        {
            writeTasks.AddRange([
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.dtape"), generator.GenerateDanceTape(package)),
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.ktape"), generator.GenerateKaraokeTape(package)),
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.act"), generator.GenerateGenericActor("TapeCase_Template", $"{mapWorldRelative}/timeline/{mapNameLower}_tml_dance.tpl")),
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.tpl"), generator.GenerateTapeCaseTpl(mapName, "dance")),
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.act"), generator.GenerateGenericActor("TapeCase_Template", $"{mapWorldRelative}/timeline/{mapNameLower}_tml_karaoke.tpl")),
                exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.tpl"), generator.GenerateTapeCaseTpl(mapName, "karaoke"))
            ]);
        }

        writeTasks.AddRange([
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tape"), generator.GenerateMainSequenceTape(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.act"), generator.GenerateGenericActor("MasterTape", $"{mapWorldRelative}/cinematics/{mapNameLower}_mainsequence.tpl")),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tpl"), generator.GenerateMainSequenceTpl(mapName)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.isc"), generator.GenerateMainScene(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(audioFolder, $"{mapNameLower}_audio.isc"), generator.GenerateAudioScene(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(timelineFolder, $"{mapNameLower}_tml.isc"), generator.GenerateTimelineScene(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(cinematicsFolder, $"{mapNameLower}_cine.isc"), generator.GenerateCinematicsScene(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(menuartFolder, $"{mapNameLower}_menuart.isc"), generator.GenerateMenuArtScene(package)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(graphFolder, $"{mapNameLower}_graph.isc"), generator.GenerateGraphScene(mapName)),
            exporter.WriteEngineResourceAsync(ctx, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.sgs"), generator.GenerateSgs())
        ]);

        if (writeAutodanceResources)
        {
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.tpl"), generator.GenerateAutodanceTape(package)));
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.isc"), generator.GenerateAutodanceScene(package)));
        }

        if (platform != UbiArtPlatform.Uncooked)
        {
            writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video.isc"), generator.GenerateVideoScene(mapName)));
            // Only Modern engines typically use map preview video scenes, but for 2015/Wii support we assume standard structure if generator supports it
            bool writeVideoMapPreviewScene = version > UbiArtEngineVersion.JD2015 && (!isLegacyFormat || platform == UbiArtPlatform.Xenon);
            if (writeVideoMapPreviewScene)
                writeTasks.Add(exporter.WriteEngineResourceAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}_video_map_preview.isc"), generator.GenerateVideoMapPreviewScene(mapName)));

            if (!isLegacyFormat || version > UbiArtEngineVersion.JD2015)
                writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, "video_player_main.act"), generator.GenerateVideoPlayerActor(mapName, false)));
            // Only write preview actor if modern or if generator provides it (JD2015 generator might return empty or logic check)
            if (version > UbiArtEngineVersion.JD2015 && !isLegacyFormat)
                writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, "video_player_map_preview.act"), generator.GenerateVideoPlayerActor(mapName, true)));

            if (!isLegacyFormat)
                writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(videosFolder, $"{mapNameLower}.mpd"), generator.GenerateMpd()));

            if (writeAutodanceResources)
                writeTasks.Add(exporter.WriteBinaryFileAsync(ctx, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.act"), generator.GenerateAutodanceActor(mapName)));
        }

        await Task.WhenAll(writeTasks);
    }

    private async Task PrepareAndWriteAudioAsync(IntermediateSongPackage package, string? materializedRoot, string mapWorldBase, ExportContext ctx, IPlatformExporter exporter, IEngineContentGenerator generator)
    {
        if (string.IsNullOrEmpty(materializedRoot))
            return;
        string sourceFile = IntermediatePackageLayout.Resolve(materializedRoot, IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!ctx.IO.FileExists(sourceFile))
            return;

        string audioFolderRel = Path.Combine(mapWorldBase, "audio");
        string ambFolderRel = Path.Combine(audioFolderRel, "amb");
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

        double startBeat = package.TimelineStructure.StartBeat;
        double cutSeconds = 0;
        if (startBeat < 0 && package.TimelineStructure.Markers.Count > 1)
        {
            cutSeconds = package.TimelineStructure.Markers[(int)Math.Abs(startBeat)] / 48000.0;
        }

        List<Task> audioTasks = [];

        if (cutSeconds > 0.001)
        {
            audioTasks.Add(exporter.WriteAudioAsync(
                ctx,
                Path.Combine(ambFolderRel, $"amb_{mapNameLower}_intro.wav"),
                new UbiArtAudioExportSource(sourceFile, Duration: TimeSpan.FromSeconds(cutSeconds))));
        }

        audioTasks.Add(exporter.WriteAudioAsync(
            ctx,
            Path.Combine(audioFolderRel, $"{mapNameLower}.wav"),
            new UbiArtAudioExportSource(sourceFile, Start: TimeSpan.FromSeconds(cutSeconds), Markers: package.TimelineStructure.Markers)));

        await Task.WhenAll(audioTasks);
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
        string pictosRel = Path.Combine(platformRoot, layout.GetPictosFolder("", mapNameLower, platform, version));
        string menuRel = Path.Combine(mapWorldBase, "menuart", "textures");
        string actorsRel = Path.Combine(mapWorldBase, "menuart", "actors");
        bool isLegacyFormat = generator is LegacyEngineContentGenerator;

        // Create image service to access images at requested resolutions
        IntermediateImageService imageService = new(materializedRoot, package, ctx.IO);

        // 1. Pictograms
        string pictosSource = ctx.IO.Combine(materializedRoot, "assets", "pictograms");
        if (ctx.IO.DirectoryExists(pictosSource))
        {
            string pictoExtension = version == UbiArtEngineVersion.JD2014 ? ".tga" : ".png";
            await Parallel.ForEachAsync(ctx.IO.GetFiles(pictosSource), async (file, cancellationToken) =>
            {
                using Image<Bgra32> img = await Image.LoadAsync<Bgra32>(file, cancellationToken);
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                await exporter.WriteTextureAsync(ctx, Path.Combine(pictosRel, $"{name}{pictoExtension}"), img);
            });
        }

        // 2. Coach Textures (1024x1024)
        await Parallel.ForEachAsync(Enumerable.Range(1, package.Metadata.CoachCount), async (coachIndex, cancellationToken) =>
        {
            using Image<Bgra32> coach = await imageService.GetCoachAsync(coachIndex, width: 1024, height: 1024, useFadeEffect: true, cancellationToken: cancellationToken);
            await WriteMenuArtTextureAsync($"{mapNameLower}_coach_{coachIndex}", coach, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        });

        // 3. Cover Textures - all derived from square cover at different sizes
        if (ShouldWriteMenuArtTexture($"{mapNameLower}_cover_generic", platform, version, isLegacyFormat))
        {
            using Image<Bgra32> coverGeneric = await imageService.GetSquareCoverAsync(width: 512, height: 512);
            await WriteMenuArtTextureAsync($"{mapNameLower}_cover_generic", coverGeneric, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        if (ShouldWriteMenuArtTexture($"{mapNameLower}_cover_online", platform, version, isLegacyFormat))
        {
            using Image<Bgra32> coverOnline = await imageService.GetSquareCoverAsync(width: 256, height: 256);
            await WriteMenuArtTextureAsync($"{mapNameLower}_cover_online", coverOnline, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        if (ShouldWriteMenuArtTexture($"{mapNameLower}_cover_online_kids", platform, version, isLegacyFormat))
        {
            using Image<Bgra32> coverKids = await imageService.GetSquareCoverAsync(width: 256, height: 256);
            await WriteMenuArtTextureAsync($"{mapNameLower}_cover_online_kids", coverKids, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        // 4. Map Background (2048x1024)
        if (ShouldWriteMenuArtTexture($"{mapNameLower}_map_bkg", platform, version, isLegacyFormat))
        {
            using Image<Bgra32> mapBkg = await imageService.GetMapBackgroundAsync(width: 2048, height: 1024);
            await WriteMenuArtTextureAsync($"{mapNameLower}_map_bkg", mapBkg, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        // 5. Album Background (256x256 center-cropped square)
        using (Image<Bgra32> albumBkg = await imageService.GetAlbumBackgroundAsync(width: 256, height: 256))
        {
            await WriteMenuArtTextureAsync($"{mapNameLower}_cover_albumbkg", albumBkg, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        // 6. Banner (1024x512)
        if (ShouldWriteMenuArtTexture($"{mapNameLower}_banner_bkg", platform, version, isLegacyFormat))
        {
            using Image<Bgra32> banner = await imageService.GetBannerAsync(width: 1024, height: 512);
            await WriteMenuArtTextureAsync($"{mapNameLower}_banner_bkg", banner, menuRel, actorsRel, ctx, exporter, generator, package, platform);
        }

        // 7. Album Coach (1024x1024 composite)
        using Image<Bgra32> albumCoach = await imageService.GetAlbumCoachAsync(width: 1024, height: 1024);
        await WriteMenuArtTextureAsync($"{mapNameLower}_cover_albumcoach", albumCoach, menuRel, actorsRel, ctx, exporter, generator, package, platform);
    }

    private async Task WriteMenuArtTextureAsync(
        string textureName,
        Image<Bgra32> image,
        string menuRel,
        string actorsRel,
        ExportContext ctx,
        IPlatformExporter exporter,
        IEngineContentGenerator generator,
        IntermediateSongPackage package,
        UbiArtPlatform platform)
    {
        // Write texture
        await exporter.WriteTextureAsync(ctx, Path.Combine(menuRel, $"{textureName}.tga"), image);

        // Write actor (only for Cooked platforms)
        if (platform != UbiArtPlatform.Uncooked)
        {
            await exporter.WriteBinaryFileAsync(ctx, Path.Combine(actorsRel, $"{textureName}.act"), generator.GenerateMenuArtActor(textureName, package.Metadata.MapName));
        }
    }

    private async Task GenerateColorsAsync(IntermediateSongPackage package, string materializedRoot, IFileSystem io)
    {
        if (package.Metadata.AdditionalMetadata.ContainsKey("songcolor_1a"))
            return;

        try
        {
            IntermediateImageService imageService = new(materializedRoot, package, io);
            using Image<Bgra32> img = await imageService.GetMapBackgroundAsync(width: 2048, height: 1024);

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
            logger.LogWarning("Failed to generate song colors from map background: {Message}", ex.Message);
        }
    }

    private static bool ShouldWriteAutodanceResources(UbiArtPlatform platform, bool isLegacyFormat)
        => !isLegacyFormat || platform != UbiArtPlatform.Revolution;

    private static bool ShouldWriteSoundSequence(IntermediateSongPackage package)
    {
        bool hasIntroAmbience = package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1;
        return hasIntroAmbience ||
               package.Vibrations.Clips.Count > 0 ||
               package.HideUserInterface.Clips.Count > 0;
    }

    private static bool ShouldWriteMenuArtTexture(string textureName, UbiArtPlatform platform, UbiArtEngineVersion version, bool isLegacyFormat)
    {
        if (!isLegacyFormat)
        {
            if (textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase))
                return version >= UbiArtEngineVersion.JD2020;

            return true;
        }

        if (version == UbiArtEngineVersion.JD2014 &&
            (textureName.EndsWith("_cover_generic", StringComparison.OrdinalIgnoreCase) ||
             textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (textureName.EndsWith("_banner_bkg", StringComparison.OrdinalIgnoreCase))
            return false;

        if (textureName.EndsWith("_cover_online", StringComparison.OrdinalIgnoreCase) ||
            textureName.EndsWith("_cover_online_kids", StringComparison.OrdinalIgnoreCase))
            return false;

        if (textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase))
            return platform == UbiArtPlatform.Revolution;

        return true;
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
            string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
            string destFileName = GetUbiArtVideoFileName(mapNameLower, platform, version);
            JdiVideoEncodeRequest request = BuildUbiArtVideoRequest(materializedRoot, destFileName, platform, version);

            string? sourceFile = await JdiVideoConverter.GetOrCreateVideoAsync(request, logger);

            if (sourceFile != null && ctx.IO.FileExists(sourceFile))
            {
                string relFolder = Path.Combine(rawMapWorldBase, "videoscoach");
                string destPath = Path.Combine(relFolder, destFileName);
                string fullDest = ctx.IO.Combine(ctx.OutputFolder, destPath);

                ctx.IO.CreateDirectory(Path.GetDirectoryName(fullDest) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullDest}'."));
                ctx.IO.Copy(sourceFile, fullDest, true);

                logger.LogInformation("Video exported to {Path} (Input: {Input})", destFileName, Path.GetFileName(sourceFile));
            }
        }

        CopyRawMoveAssets(materializedRoot, rawMapWorldBase, ctx, platform);
    }

    private static JdiVideoEncodeRequest BuildUbiArtVideoRequest(string materializedRoot, string destFileName, UbiArtPlatform platform, UbiArtEngineVersion version)
    {
        if (platform == UbiArtPlatform.Revolution)
        {
            return new JdiVideoEncodeRequest(materializedRoot, destFileName, ".webm", "vp8")
            {
                Transform = new JdiVideoTransform
                {
                    Width = 512,
                    Height = 384,
                    ScaleAlgorithm = JdiScaleAlgorithm.Bicubic,
                    SampleAspectRatio = "1",
                    DisplayAspectRatio = "4/3"
                },
                Encoding = new JdiVideoEncodingSettings
                {
                    UseDefaultCodecTuning = false,
                    Profile = 2,
                    PixelFormat = "yuv420p",
                    AutoAltRef = false,
                    Bitrate = 2_000_000,
                    Quality = "good",
                    CpuUsed = 16
                },
                ForceTranscode = true
            };
        }

        string codec = version == UbiArtEngineVersion.JD2017 ? "vp8" : "vp9";
        return new JdiVideoEncodeRequest(materializedRoot, destFileName, ".webm", codec);
    }

    private static string GetUbiArtVideoFileName(string mapNameLower, UbiArtPlatform platform, UbiArtEngineVersion version)
        => platform switch
        {
            UbiArtPlatform.NX when version != UbiArtEngineVersion.JD2017 => $"{mapNameLower}.vp9.720.webm",
            UbiArtPlatform.Revolution => $"{mapNameLower}.wii.webm",
            UbiArtPlatform.Xenon => $"{mapNameLower}.x360.webm",
            UbiArtPlatform.Cell => $"{mapNameLower}.ps3.webm",
            _ => $"{mapNameLower}.webm"
        };

    private static void CopyRawMoveAssets(string materializedRoot, string rawMapWorldBase, ExportContext ctx, UbiArtPlatform platform)
    {
        string movesSource = ctx.IO.Combine(materializedRoot, "assets", "moves");
        if (ctx.IO.DirectoryExists(movesSource))
        {
            string movesFolder = Path.Combine(rawMapWorldBase, "timeline", "moves", GetHandMovePlatformFolder(platform));
            CopyRawFiles(ctx, movesSource, "*.msm", movesFolder);
        }

        string gesturesSource = ctx.IO.Combine(materializedRoot, "assets", "gestures");
        if (ctx.IO.DirectoryExists(gesturesSource))
        {
            string gesturesFolder = Path.Combine(rawMapWorldBase, "timeline", "moves", GetFullBodyMovePlatformFolder(platform));
            CopyRawFiles(ctx, gesturesSource, "*.gesture", gesturesFolder);
        }
    }

    private static void CopyRawFiles(ExportContext ctx, string sourceFolder, string pattern, string relativeDestinationFolder)
    {
        ctx.IO.CreateDirectory(ctx.IO.Combine(ctx.OutputFolder, relativeDestinationFolder));

        Parallel.ForEach(ctx.IO.GetFiles(sourceFolder, pattern), sourceFile =>
        {
            string fileName = Path.GetFileName(sourceFile).ToLowerInvariant();
            string destPath = Path.Combine(relativeDestinationFolder, fileName);
            string fullDest = ctx.IO.Combine(ctx.OutputFolder, destPath);

            ctx.IO.Copy(sourceFile, fullDest, true);
        });
    }

    private static string GetHandMovePlatformFolder(UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Revolution => "wii",
        UbiArtPlatform.Cell => "ps3",
        UbiArtPlatform.Xenon => "x360",
        _ => "wiiu"
    };

    private static string GetFullBodyMovePlatformFolder(UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Durango => "durango",
        UbiArtPlatform.Orbis => "orbis",
        UbiArtPlatform.Xenon => "x360",
        _ => GetHandMovePlatformFolder(platform)
    };

    private static async Task WriteUncookedTrkFileAsync(IntermediateSongPackage package, ExportContext ctx, string trkPath)
    {
        StringBuilder trkBuilder = new();
        trkBuilder.AppendLine("structure = { MusicTrackStructure = {");

        // markers
        trkBuilder.AppendLine("markers = {");
        foreach (int m in package.TimelineStructure.Markers)
        {
            trkBuilder.AppendLine($"    {{ VAL = {m} }},");
        }

        trkBuilder.AppendLine("},");

        // signatures
        trkBuilder.AppendLine("signatures = {");
        foreach (SignatureSegment s in package.TimelineStructure.Signatures)
        {
            string comment = EscapeLuaString(s.Comment ?? string.Empty);
            string markerStr = s.Marker.ToString(CultureInfo.InvariantCulture);
            trkBuilder.AppendLine($"    {{ MusicSignature = {{ beats = {s.Beats}, marker = {markerStr}, comment = \"{comment}\" }} }},");
        }

        trkBuilder.AppendLine("},");

        // sections
        trkBuilder.AppendLine("sections = {");
        foreach (SectionSegment sec in package.TimelineStructure.Sections)
        {
            string comment = EscapeLuaString(sec.Comment ?? string.Empty);
            string markerStr = sec.StartBeat.ToString(CultureInfo.InvariantCulture);
            trkBuilder.AppendLine($"    {{ MusicSection = {{ sectionType = {(int)sec.SectionType}, marker = {markerStr}, comment = \"{comment}\" }} }},");
        }

        trkBuilder.AppendLine("},");

        // comments
        trkBuilder.AppendLine("comments = {},");

        // basic fields
        trkBuilder.AppendLine($"startBeat = {package.TimelineStructure.StartBeat},");
        trkBuilder.AppendLine($"endBeat = {package.TimelineStructure.EndBeat},");
        trkBuilder.AppendLine($"videoStartTime = {package.TimelineStructure.VideoStartOffset.ToString(CultureInfo.InvariantCulture)},");
        trkBuilder.AppendLine($"previewEntry = {package.TimelineStructure.PreviewEntryBeat},");
        trkBuilder.AppendLine($"previewLoopStart = {package.TimelineStructure.PreviewLoopStartBeat},");
        trkBuilder.AppendLine($"previewLoopEnd = {package.TimelineStructure.PreviewLoopEndBeat},");
        trkBuilder.AppendLine($"previewDuration = {package.TimelineStructure.PreviewDuration},");

        trkBuilder.AppendLine("} } ");

        string fullPath = ctx.IO.Combine(ctx.OutputFolder, trkPath);
        ctx.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllTextAsync(fullPath, trkBuilder.ToString());
    }

    private static async Task WriteUncookedAmbFilesAsync(IntermediateSongPackage package, ExportContext ctx, string ambFolder, string mapName)
    {
        string mapNameLower = mapName.ToLowerInvariant();
        string audioWorldPath = $"world/maps/{mapNameLower}/audio/amb/amb_{mapName}_intro.wav";

        // Write .ilu file
        string iluContent = $@"DESCRIPTOR = 
{{
    {{
		SoundDescriptor_Template=
		{{
			name=""amb_{mapName}_intro"",  
			volume=-50,
			category=""AMB"",
			limitMode=LimiterMode.RejectNew,
			params=
			{{SoundParams={{
				numChannels=2,
				loop=0, 
				playMode=PlayMode.Random,
				randomVolMin=0.0,
				randomVolMax=0.0,
				randomPitchMin=1.0,
				randomPitchMax=1.0,
				fadeInTime=0.0,
				fadeOutTime=0.0,
			}}}},
			files=
			{{
				{{
					VAL=""{audioWorldPath}"",
				}},
			}},
		}}
	}},
}}

appendTable(component.SoundComponent_Template.soundList,DESCRIPTOR)";

        string iluPath = Path.Combine(ambFolder, $"AMB_{mapName}_Intro.ilu");
        string fullIluPath = ctx.IO.Combine(ctx.OutputFolder, iluPath);
        ctx.IO.CreateDirectory(Path.GetDirectoryName(fullIluPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullIluPath}'."));
        await File.WriteAllTextAsync(fullIluPath, iluContent);

        // Write .tpl file
        string tplContent = $@"params=
{{
	NAME=""Actor_Template"",
	Actor_Template=
	{{
		COMPONENTS=
		{{
		}}
	}}
}}
includeReference(""world/maps/{mapNameLower}/audio/amb/amb_{mapName}_intro.wav"")
";

        string tplPath = Path.Combine(ambFolder, $"AMB_{mapName}_Intro.tpl");
        string fullTplPath = ctx.IO.Combine(ctx.OutputFolder, tplPath);
        await File.WriteAllTextAsync(fullTplPath, tplContent);
    }

    private static string EscapeLuaString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}