using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnitySongTitleGenerationRequest(
    string SongName,
    UnityExportData UnityData,
    string MenuArtFolder,
    bool AllowOnlineLookup,
    string TemplatePath,
    string OutputFolder,
    bool ForCustomServer);

public static class UnitySongTitleGenerator
{
    public static Task GenerateAsync(UnitySongTitleGenerationRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnitySongTitleGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        UnityCoverArtRequest coverRequest = new(request.UnityData, request.MenuArtFolder);
        using Image<Rgba32>? titleImage = PrepareSongTitleImage(request, coverRequest);
        if (titleImage == null)
        {
            Logger.Log("No song title logo image found, skipping song title bundle generation.", LogLevel.Important);
            return;
        }

        UnitySongTitleBundleRequest builderRequest = new(
            request.SongName,
            titleImage,
            request.TemplatePath,
            request.OutputFolder,
            request.ForCustomServer);

        SongTitleBundleBuilder.GenerateSongTitleLogo(builderRequest);
    }

    private static Image<Rgba32>? PrepareSongTitleImage(UnitySongTitleGenerationRequest request, UnityCoverArtRequest coverRequest)
    {
        Directory.CreateDirectory(request.MenuArtFolder);

        Image<Rgba32>? image = null;
        if (request.AllowOnlineLookup)
            image = UnityCoverArtGenerator.TryImageWeb(request.SongName, "Title");

        image ??= UnityCoverArtGenerator.ExistingSongTitleLogo(coverRequest);
        return image;
    }
}
