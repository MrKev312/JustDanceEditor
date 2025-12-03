using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityCoverGenerationRequest(
    string SongName,
    UnityExportData UnityData,
    string MenuArtFolder,
    bool AllowOnlineLookup,
    string TemplatePath,
    string OutputFolder,
    bool ForCustomServer);

public static class UnityCoverGenerator
{
    public static Task GenerateAsync(UnityCoverGenerationRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityCoverGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MenuArtFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        UnityCoverArtRequest coverRequest = new(request.UnityData, request.MenuArtFolder);

        using Image<Rgba32>? coverImage = PrepareCoverImage(request, coverRequest);
        if (coverImage == null)
        {
            Logger.Log("Cover image could not be prepared, skipping cover bundle generation.", LogLevel.Warning);
            return;
        }

        UnityCoverBundleRequest builderRequest = new(
            request.SongName,
            coverImage,
            request.TemplatePath,
            request.OutputFolder,
            request.ForCustomServer);

        CoverBundleBuilder.GenerateCoverBundle(builderRequest);
    }

    private static Image<Rgba32>? PrepareCoverImage(UnityCoverGenerationRequest request, UnityCoverArtRequest coverRequest)
    {
        Directory.CreateDirectory(request.MenuArtFolder);

        Image<Rgba32>? image = null;
        if (request.AllowOnlineLookup)
            image = UnityCoverArtGenerator.TryImageWeb(request.SongName, "Cover");

        image ??= UnityCoverArtGenerator.ExistingCover(coverRequest);
        image ??= UnityCoverArtGenerator.GenerateOwnCover(coverRequest);

        if (image == null)
            return null;

        string tempCoverPath = Path.Combine(request.MenuArtFolder, $"Cover_{request.SongName}.png");
        image.Mutate(o => o.Resize(640, 360));
        image.Save(tempCoverPath);
        Logger.Log($"Cover image prepared at {tempCoverPath}", LogLevel.Debug);
        return image;
    }
}
