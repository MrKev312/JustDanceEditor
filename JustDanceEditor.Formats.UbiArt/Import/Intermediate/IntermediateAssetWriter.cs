using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import.Audio;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Import.Recordings;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.Audio.NAudio;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class IntermediateAssetWriter
{
    public static async Task PopulateFromUbiArtAsync(
        ConversionContext context,
        IntermediateSongPackage package,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IAudioConverter audioConverter,
        IFileSystem? io = null)
    {
        IFileSystem fileSystem = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(audioConverter);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;
        IntermediateAssetPaths.ResetAssetsRoot(packageRoot, fileSystem);

        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");
        string audioFolder = IntermediateAssetPaths.EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder, fileSystem);
        string videoFolder = IntermediateAssetPaths.EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder, fileSystem);
        string previewVideoFolder = IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        IntermediateAssetPaths.TryDeleteDirectory(previewVideoFolder, logger, fileSystem);

        Task pictogramTask = PictogramAssetImporter.ImportAsync(context, packageRoot, logger, textureService, fileSystem);
        Task audioTask = AudioConverter.ConvertAudioAsync(songData, context.FileSystem, new AudioConversionOptions
        {
            MasterOutputFolder = audioFolder,
            PreviewOutputFolder = audioFolder
        }, audioConverter, logger);
        Task videoTask = UbiArtVideoImporter.ImportMasterAsync(
            context.FileSystem,
            songData,
            package,
            videoFolder,
            logger,
            textureService,
            fileSystem,
            context.Request.RenderVideoSpeedTest);
        Task supplementalAssetTask = ImportSupplementalAssetsAsync(context, packageRoot, logger, textureService, fileSystem);
        Task recordingTask = UbiArtRecordingImporter.ImportAsync(context, packageRoot, logger);

        await Task.WhenAll(pictogramTask, audioTask, videoTask, supplementalAssetTask, recordingTask);
    }

    internal static string BuildGraphVideoFilter(int sourceWidth, int sourceHeight, CinematicSingleVideoScene scene) =>
        GraphVideoCropPlanner.BuildFilter(sourceWidth, sourceHeight, scene);

    internal static Rectangle CalculateGraphSourceCrop(int sourceWidth, int sourceHeight, CinematicSingleVideoScene scene) =>
        GraphVideoCropPlanner.CalculateSourceCrop(sourceWidth, sourceHeight, scene);

    internal static double GetMasterVideoDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds) =>
        IntermediateVideoTiming.GetMasterDurationSeconds(package, fallbackDurationSeconds);

    internal static double GetCinematicRenderDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds) =>
        IntermediateVideoTiming.GetCinematicRenderDurationSeconds(package, fallbackDurationSeconds);

    private static async Task ImportSupplementalAssetsAsync(
        ConversionContext context,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        Task brandingTask = Task.Run(() => BrandingAssetImporter.Import(context, packageRoot, logger, textureService, io));
        Task coachTask = Task.Run(() => CoachAssetImporter.Import(context, packageRoot, logger, textureService, io));
        Task motionTask = Task.Run(() => MotionAssetImporter.Import(context, packageRoot, io));
        await Task.WhenAll(brandingTask, coachTask, motionTask);
    }
}
