using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityCoachesSmallGenerationRequest(
    string SongName,
    int CoachCount,
    UnityMenuArtSource MenuArt,
    string TemplatePath,
    string OutputFolder,
    bool ForCustomServer);

public static class UnityCoachesSmallGenerator
{
    public static Task GenerateAsync(UnityCoachesSmallGenerationRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityCoachesSmallGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SongName))
            throw new ArgumentException("Song name is required", nameof(request));
        if (request.CoachCount <= 0)
            throw new ArgumentException("Coach count must be greater than zero", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        Logger.Log($"Converting CoachesSmall for {request.SongName}...");

        List<Image<Rgba32>> coachImages = [];
        try
        {
            LoadCoachImages(request, coachImages);

            UnityCoachesSmallBundleRequest builderRequest = new(
                request.SongName,
                coachImages,
                request.TemplatePath,
                request.OutputFolder,
                request.ForCustomServer);

            CoachesSmallBundleBuilder.Generate(builderRequest);
        }
        finally
        {
            foreach (Image<Rgba32> image in coachImages)
                image.Dispose();
        }

        Logger.Log("Finished generating CoachesSmall");
    }

    private static void LoadCoachImages(UnityCoachesSmallGenerationRequest request, List<Image<Rgba32>> destination)
    {
        IReadOnlyList<string> coachFiles = request.MenuArt.CoachImagePaths;
        if (coachFiles.Count == 0)
            throw new FileNotFoundException("No coach images defined in the intermediate package.");

        for (int i = 0; i < request.CoachCount; i++)
        {
            if (i >= coachFiles.Count)
                throw new InvalidOperationException($"Not enough coach images available. Needed {request.CoachCount}, found {coachFiles.Count}.");

            destination.Add(Image.Load<Rgba32>(coachFiles[i]));
        }
    }
}
