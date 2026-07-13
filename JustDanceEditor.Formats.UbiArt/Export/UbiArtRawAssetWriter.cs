using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed class UbiArtRawAssetWriter(ILogger logger)
{
    private static readonly string[] SupportedVideoExtensions = [".webm", ".mp4", ".mkv", ".avi", ".mov"];

    public void ConfigureUncookedVideoSource(UbiArtExportPlan plan)
    {
        if (plan.Platform != UbiArtPlatform.Uncooked ||
            plan.EngineGenerator is not UncookedEngineContentGenerator generator ||
            string.IsNullOrWhiteSpace(plan.MaterializedRoot))
        {
            return;
        }

        string videoSourceFolder = plan.Context.IO.Combine(plan.MaterializedRoot, "assets", "video");
        if (!plan.Context.IO.DirectoryExists(videoSourceFolder))
            return;

        string? source = FindVideoSource(plan.Context.IO, videoSourceFolder, plan.MapNameLower);
        if (source != null)
            generator.VideoFileName = $"{plan.MapNameLower}{Path.GetExtension(source).ToLowerInvariant()}";
    }

    public async Task WriteAsync(UbiArtExportPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.MaterializedRoot))
            return;

        string videoSourceFolder = plan.Context.IO.Combine(plan.MaterializedRoot, "assets", "video");
        if (plan.Context.IO.DirectoryExists(videoSourceFolder))
            await WriteVideoAsync(plan, videoSourceFolder);

        CopyMoveAssets(plan);
    }

    private async Task WriteVideoAsync(UbiArtExportPlan plan, string videoSourceFolder)
    {
        string? sourceFile;
        string destinationFileName;
        if (plan.Platform == UbiArtPlatform.Uncooked)
        {
            sourceFile = FindVideoSource(plan.Context.IO, videoSourceFolder, plan.MapNameLower);
            destinationFileName = sourceFile == null
                ? $"{plan.MapNameLower}.webm"
                : $"{plan.MapNameLower}{Path.GetExtension(sourceFile).ToLowerInvariant()}";
        }
        else
        {
            destinationFileName = GetVideoFileName(plan.MapNameLower, plan.Platform, plan.EngineVersion);
            JdiVideoEncodeRequest request = BuildVideoRequest(plan.MaterializedRoot!, destinationFileName, plan.Platform, plan.EngineVersion);
            sourceFile = await JdiVideoConverter.GetOrCreateVideoAsync(request, logger);
        }

        if (sourceFile == null || !plan.Context.IO.FileExists(sourceFile))
            return;

        string destinationPath = Path.Combine(plan.RawMapWorldBase, "videoscoach", destinationFileName);
        string fullDestination = plan.Context.IO.Combine(plan.Context.OutputFolder, destinationPath);
        plan.Context.IO.CreateDirectory(Path.GetDirectoryName(fullDestination) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullDestination}'."));
        plan.Context.IO.Copy(sourceFile, fullDestination, true);

        logger.LogInformation(
            plan.Platform == UbiArtPlatform.Uncooked
                ? "Video copied unchanged to {Path} (Input: {Input})"
                : "Video exported to {Path} (Input: {Input})",
            destinationFileName,
            Path.GetFileName(sourceFile));
    }

    private static string? FindVideoSource(IFileSystem io, string videoSourceFolder, string mapNameLower)
        => io.GetFiles(videoSourceFolder)
            .Where(file => SupportedVideoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(file => Path.GetFileNameWithoutExtension(file).Equals(mapNameLower, StringComparison.OrdinalIgnoreCase))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static JdiVideoEncodeRequest BuildVideoRequest(
        string materializedRoot,
        string destinationFileName,
        UbiArtPlatform platform,
        UbiArtEngineVersion version)
    {
        if (platform == UbiArtPlatform.Revolution)
        {
            return new JdiVideoEncodeRequest(materializedRoot, destinationFileName, ".webm", "vp8")
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
        return new JdiVideoEncodeRequest(materializedRoot, destinationFileName, ".webm", codec);
    }

    private static string GetVideoFileName(string mapNameLower, UbiArtPlatform platform, UbiArtEngineVersion version)
        => platform switch
        {
            UbiArtPlatform.NX when version != UbiArtEngineVersion.JD2017 => $"{mapNameLower}.vp9.720.webm",
            UbiArtPlatform.Revolution => $"{mapNameLower}.wii.webm",
            UbiArtPlatform.Xenon => $"{mapNameLower}.x360.webm",
            UbiArtPlatform.Cell => $"{mapNameLower}.ps3.webm",
            _ => $"{mapNameLower}.webm"
        };

    private static void CopyMoveAssets(UbiArtExportPlan plan)
    {
        string movesVersionFolder = plan.EngineVersion switch
        {
            UbiArtEngineVersion.JD2014 => IntermediatePackageLayout.Assets.MovesV5Folder,
            UbiArtEngineVersion.JD2015 when plan.Platform == UbiArtPlatform.Uncooked => IntermediatePackageLayout.Assets.MovesV6Folder,
            _ => IntermediatePackageLayout.Assets.MovesV7Folder
        };
        string movesSource = ResolveMaterializedPath(plan, movesVersionFolder);
        if (plan.Context.IO.DirectoryExists(movesSource))
        {
            string destination = Path.Combine(plan.RawMapWorldBase, "timeline", "moves", GetHandMovePlatformFolder(plan.Platform));
            CopyFiles(plan.Context, movesSource, "*.msm", destination);
        }

        string gesturesRoot = ResolveMaterializedPath(plan, IntermediatePackageLayout.Assets.GesturesFolder);
        if (!plan.Context.IO.DirectoryExists(gesturesRoot))
            return;

        if (plan.Platform != UbiArtPlatform.Uncooked)
        {
            if (UbiArtGestureFolders.TryGetPlatformFolder(plan.Platform, out string? platformFolder) && platformFolder != null)
            {
                string platformSource = ResolveMaterializedPath(plan, UbiArtGestureFolders.PackageFolder(platformFolder));
                if (plan.Context.IO.DirectoryExists(platformSource))
                    CopyFiles(plan.Context, platformSource, "*.gesture", Path.Combine(plan.RawMapWorldBase, "timeline", "moves", platformFolder));
            }

            return;
        }

        foreach (string gesturesSource in plan.Context.IO.GetDirectories(gesturesRoot))
        {
            string platformFolder = Path.GetFileName(gesturesSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(platformFolder))
                CopyFiles(plan.Context, gesturesSource, "*.gesture", Path.Combine(plan.RawMapWorldBase, "timeline", "moves", platformFolder));
        }
    }

    private static void CopyFiles(ExportContext context, string sourceFolder, string pattern, string relativeDestinationFolder)
    {
        context.IO.CreateDirectory(context.IO.Combine(context.OutputFolder, relativeDestinationFolder));
        Parallel.ForEach(context.IO.GetFiles(sourceFolder, pattern), sourceFile =>
        {
            string destination = context.IO.Combine(
                context.OutputFolder,
                relativeDestinationFolder,
                Path.GetFileName(sourceFile).ToLowerInvariant());
            context.IO.Copy(sourceFile, destination, true);
        });
    }

    private static string GetHandMovePlatformFolder(UbiArtPlatform platform)
        => platform switch
        {
            UbiArtPlatform.Revolution => "wii",
            UbiArtPlatform.Cell => "ps3",
            UbiArtPlatform.Xenon => "x360",
            _ => "wiiu"
        };

    private static string ResolveMaterializedPath(UbiArtExportPlan plan, string relativePath)
        => plan.Context.IO.Combine([plan.MaterializedRoot!, .. relativePath.Split('/')]);
}
