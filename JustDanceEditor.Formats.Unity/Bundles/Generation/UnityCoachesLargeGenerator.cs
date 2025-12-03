using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityCoachesLargeGenerationRequest(
    string SongName,
    int CoachCount,
    string MenuArtFolder,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MenuArtFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        Logger.Log($"Converting CoachesLarge for {request.SongName}...");

        List<Image<Rgba32>> coachImages = [];
        Image<Rgba32>? background = null;

        try
        {
            for (int i = 1; i <= request.CoachCount; i++)
            {
                string imagePath = Path.Combine(request.MenuArtFolder, $"{request.SongName}_Coach_{i}.png");
                if (!File.Exists(imagePath))
                    throw new FileNotFoundException($"Coach image not found: {imagePath}");

                coachImages.Add(Image.Load<Rgba32>(imagePath));
            }

            UnityCoverArtRequest coverRequest = new(request.UnityData, request.MenuArtFolder);
            background = UnityCoverArtGenerator.GetBackground(coverRequest);

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
}
