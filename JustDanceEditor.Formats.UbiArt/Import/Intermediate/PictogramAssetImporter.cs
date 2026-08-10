using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;
using JustDanceEditor.Formats.UbiArt.Import.Core;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using UbiArtPictogramClip = JustDanceEditor.Formats.UbiArt.Model.Clips.PictogramClip;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class PictogramAssetImporter
{
    public static async Task ImportAsync(
        ConversionContext context,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        CookedFile[] folderPictograms = context.FileSystem.AssetResolver?.GetPictograms() ?? [];
        CookedFile[] referencedPictograms = [.. EnumerateReferencedFiles(context)];
        CookedFile[] files = [.. folderPictograms
            .Concat(referencedPictograms)
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

        string outputFolder = IntermediateAssetPaths.EnsureFolder(
            packageRoot,
            IntermediatePackageLayout.Assets.PictogramsFolder,
            io);
        UbiArtPictoConversionRequest request = new(context.SongData, files, outputFolder);
        await Task.Run(() => UbiArtPictoConverter.Convert(request, logger, textureService, context.FileSystem, io));
    }

    private static IEnumerable<CookedFile> EnumerateReferencedFiles(ConversionContext context)
    {
        if (context.SongData == null)
            yield break;

        foreach (UbiArtPictogramClip clip in context.SongData.Clips.OfType<UbiArtPictogramClip>())
        {
            if (IntermediateAssetPaths.TryResolveReferencedCookedFile(context.FileSystem, clip.PictoPath, out CookedFile? file) &&
                file != null)
            {
                yield return file;
            }
        }
    }
}
