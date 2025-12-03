using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.Unity.Bundles.Generation;

public sealed record UnityMapPackageGenerationRequest(
    string SongName,
    UnityExportData UnityData,
    IReadOnlyList<string> PictoFiles,
    string PictoTempFolder,
    string PictoAtlasFolder,
    string? MovesFolder,
    string TemplatePath,
    string OutputFolder,
    bool ForCustomServer);

public static class UnityMapPackageGenerator
{
    public static Task GenerateAsync(UnityMapPackageGenerationRequest request) =>
        Task.Run(() => Generate(request));

    public static void Generate(UnityMapPackageGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SongName);
        ArgumentNullException.ThrowIfNull(request.UnityData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoAtlasFolder);

        UnityPictoConversionRequest pictoRequest = new(
            request.UnityData,
            request.PictoFiles ?? Array.Empty<string>(),
            request.PictoTempFolder,
            request.PictoAtlasFolder);

        Logger.Log($"Converting MapPackage for {request.SongName}...");

        UnityPictoConversionResult pictoResult = UnityPictoConverter.Convert(pictoRequest);
        List<UnityMoveFile> moveFiles = LoadMoveFiles(request.MovesFolder);

        try
        {
            UnityMapPackageBundleRequest builderRequest = new(
                request.SongName,
                request.UnityData,
                pictoResult.ImageDictionary,
                pictoResult.AtlasImages,
                moveFiles,
                request.TemplatePath,
                request.OutputFolder,
                request.ForCustomServer);

            MapPackageBundleBuilder.Generate(builderRequest);
        }
        finally
        {
            foreach (Image<Rgba32> atlas in pictoResult.AtlasImages)
                atlas.Dispose();
        }

        Logger.Log("Finished generating MapPackage bundle");
    }

    private static List<UnityMoveFile> LoadMoveFiles(string? movesFolder)
    {
        List<UnityMoveFile> moves = [];
        if (string.IsNullOrWhiteSpace(movesFolder) || !Directory.Exists(movesFolder))
        {
            Logger.Log("Moves folder not found, skipping dance move asset addition.", LogLevel.Info);
            return moves;
        }

        foreach (string path in Directory.GetFiles(movesFolder, "*.msm"))
        {
            byte[] content = File.ReadAllBytes(path);
            moves.Add(new UnityMoveFile(Path.GetFileName(path), content));
        }

        return moves;
    }
}
