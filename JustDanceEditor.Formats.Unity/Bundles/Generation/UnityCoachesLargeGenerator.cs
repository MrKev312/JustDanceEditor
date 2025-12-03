using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Collections.Generic;
using System.IO;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityCoachesLargeGenerationRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    UnityExportData UnityData,
    string TemplatePath,
    string OutputFolder,
    bool ForCustomServer);

public static class UnityCoachesLargeGenerator
{
    public static Task GenerateAsync(UnityCoachesLargeGenerationRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityCoachesLargeGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UnityData == null)
            throw new ArgumentNullException(nameof(request.UnityData));
        if (string.IsNullOrWhiteSpace(request.SongName))
            throw new ArgumentException("Song name is required", nameof(request));
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        Logger.Log($"Converting CoachesLarge for {request.SongName}...");

        List<Image<Rgba32>> coachImages = [];
        Image<Rgba32>? background = null;

        try
        {
            LoadCoachImages(request, coachImages);

            UnityCoverArtRequest coverRequest = new(request.UnityData, request.MenuArt);
            background = UnityCoverArtGenerator.TryLoadBackground(coverRequest) ?? CreateFallbackBackground();

            UnityCoachesLargeBundleRequest builderRequest = new(
                request.SongName,
                coachImages,
                background,
                request.TemplatePath,
                request.OutputFolder,
                request.ForCustomServer);

            CoachesLargeBundleBuilder.Generate(builderRequest);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
            background?.Dispose();
        }

        Logger.Log("Finished generating CoachesLarge");
    }

    private static void LoadCoachImages(UnityCoachesLargeGenerationRequest request, List<Image<Rgba32>> destination)
    {
        IReadOnlyList<string> coachFiles = request.MenuArt.CoachImagePaths;
        if (coachFiles.Count == 0)
            throw new FileNotFoundException("No coach images defined in the intermediate package.");

        for (int i = 0; i < request.CoachCount; i++)
        {
            if (i >= coachFiles.Count)
                throw new InvalidOperationException($"Not enough coach images available. Needed {request.CoachCount}, found {coachFiles.Count}.");

            string file = coachFiles[i];
            destination.Add(Image.Load<Rgba32>(file));
        }
    }

    private static Image<Rgba32> CreateFallbackBackground() => new(2048, 1024, Color.Magenta);
}
