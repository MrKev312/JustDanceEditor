using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityCoachesSmallGenerationRequest(
    string SongName,
    int CoachCount,
    string MenuArtFolder,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MenuArtFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        Logger.Log($"Converting CoachesSmall for {request.SongName}...");

        List<Image<Rgba32>> coachImages = [];
        try
        {
            for (int i = 1; i <= request.CoachCount; i++)
            {
                string imagePath = Path.Combine(request.MenuArtFolder, $"{request.SongName}_Coach_{i}.png");
                if (!File.Exists(imagePath))
                    throw new FileNotFoundException($"Coach image not found: {imagePath}");

                coachImages.Add(Image.Load<Rgba32>(imagePath));
            }

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
}
