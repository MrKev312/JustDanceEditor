using JustDanceEditor.Converter.Core;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.Unity;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Converter.Intermediate;

internal static partial class IntermediateAssetWriter
{
    private static void EnsureUnityDataInitialized(ConversionContext context, IntermediateSongPackage package)
    {
        context.UnityData ??= UnityExportDataBuilder.Create(package);
    }

    private static async Task PrepareUnityPictogramsAsync(ConversionContext context)
    {
        UnityPictoConversionRequest pictoRequest = CreateUnityPictoRequest(context);
        UnityPictoConversionResult pictoResult = await Task.Run(() => UnityPictoConverter.Convert(pictoRequest));
        foreach (Image<Rgba32> image in pictoResult.AtlasImages)
            image.Dispose();
    }

    private static UnityPictoConversionRequest CreateUnityPictoRequest(ConversionContext context)
    {
        var pictoFiles = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.PictosFolder);
        string[] pictoPaths = pictoFiles.Select(file => (string)file).ToArray();
        return new UnityPictoConversionRequest(
            context.RequireUnityData(),
            pictoPaths,
            context.FileSystem.TempFolders.PictoFolder,
            context.FileSystem.TempFolders.PictoAtlasFolder);
    }

    private static UnityCoverArtRequest CreateCoverArtRequest(ConversionContext context)
        => new(context.RequireUnityData(), context.FileSystem.TempFolders.MenuArtFolder);

    private static void AttachBrandingAssets(ConversionContext context, IntermediateAssetCatalog catalog, AssetDirectories dirs)
    {
        string? cover = ExportCoverImage(context, Path.Combine(dirs.Branding, "thumbnail.png"));
        UpdateAssetReference(catalog, "image/cover", cover, dirs, mimeType: "image/png");

        string? title = ExportSongTitleLogo(context, Path.Combine(dirs.Branding, "songTitleLogo.png"));
        UpdateAssetReference(catalog, "image/songTitleLogo", title, dirs, mimeType: "image/png");
    }

    private static string? ExportCoverImage(ConversionContext context, string destination)
    {
        UnityCoverArtRequest coverRequest = CreateCoverArtRequest(context);
        string songName = context.ResolveSongName();

        using Image<Rgba32>? cover = UnityCoverArtGenerator.ExistingCover(coverRequest)
            ?? (context.Request.OnlineCover ? UnityCoverArtGenerator.TryImageWeb(songName, "Cover") : null)
            ?? UnityCoverArtGenerator.GenerateOwnCover(coverRequest);

        if (cover == null)
        {
            Logger.Log("Cover art could not be prepared for intermediate export.", LogLevel.Warning);
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        cover.Save(destination);
        return destination;
    }

    private static string? ExportSongTitleLogo(ConversionContext context, string destination)
    {
        UnityCoverArtRequest coverRequest = CreateCoverArtRequest(context);
        string songName = context.ResolveSongName();

        using Image<Rgba32>? title = UnityCoverArtGenerator.ExistingSongTitleLogo(coverRequest)
            ?? (context.Request.OnlineCover ? UnityCoverArtGenerator.TryImageWeb(songName, "Title") : null);

        if (title == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        title.Save(destination);
        return destination;
    }
}
