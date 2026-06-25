using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.Unity.Converters;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnityJdiFormat(Func<string, IntermediateSongPackage> serverBuilder, Services.IUnityAssetMaterializer assetMaterializer, Microsoft.Extensions.Logging.ILogger<UnityJdiFormat> logger) : IJdiFormat
{
    private readonly Func<string, IntermediateSongPackage> _serverBuilder = serverBuilder;
    private readonly Services.IUnityAssetMaterializer _assetMaterializer = assetMaterializer;
    private readonly Microsoft.Extensions.Logging.ILogger<UnityJdiFormat> _logger = logger;

    public string DisplayName => "Unity";
    public bool CanImport => true;
    public bool CanExport => true;

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UnityConversionRequest unityRequest)
            throw new ArgumentException("Unity import expects a UnityConversionRequest.", nameof(request));

        _logger.LogInformation("Starting Unity -> JDI conversion from '{InputPath}'", unityRequest.InputPath);
        ValidateUnityImport(unityRequest);
        _logger.LogDebug("Validated Unity input folder '{InputPath}'", unityRequest.InputPath);

        IntermediateSongPackage package = _serverBuilder(unityRequest.InputPath);
        _logger.LogInformation("Built intermediate metadata for Unity song '{MapName}'", package.Metadata.MapName ?? package.Metadata.Title ?? "song");

        string songName = DetermineSongName(package);
        string requestedOutput = BuildSuggestedOutputFolder(unityRequest.OutputPath, songName);
        MaterializedOutputPath materializedOutput = MaterializedOutputPathResolver.Resolve(requestedOutput, unityRequest.InputPath, songName);
        string suggestedOutput = materializedOutput.Path;
        if (materializedOutput.WasRedirected)
        {
            _logger.LogWarning(
                "Unity -> JDI materialized output '{RequestedOutput}' overlaps source '{InputPath}'. Using safe materialization path '{MaterializedRoot}'.",
                requestedOutput,
                unityRequest.InputPath,
                suggestedOutput);
        }

        PrepareMaterializedDirectory(suggestedOutput);
        _logger.LogDebug("Prepared JDI materialized directory '{MaterializedRoot}'", suggestedOutput);

        Task materializeTask = Task.Run(() => _assetMaterializer.Materialize(package, unityRequest.InputPath, suggestedOutput), cancellationToken);
        Task serializeTask = Task.Run(() =>
        {
            _logger.LogDebug("Writing JDI package metadata to '{MaterializedRoot}'", suggestedOutput);
            IntermediatePackageSerializer.WriteToFolder(package, suggestedOutput);
        }, cancellationToken);

        await Task.WhenAll(materializeTask, serializeTask);

        JdiImportResult result = new(
            package,
            "Unity",
            suggestedOutput,
            MaterializedRootIsTemporary: materializedOutput.IsTemporary,
            SuggestedOutputFolder: suggestedOutput);

        _logger.LogInformation("Unity -> JDI conversion completed for '{SongName}' at '{MaterializedRoot}'", songName, suggestedOutput);
        return result;
    }

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        string songInfoPath = Path.Combine(path, "SongInfo.json");
        return File.Exists(songInfoPath);
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UnityConversionRequest unityRequest)
            throw new ArgumentException("Unity export expects a UnityConversionRequest.", nameof(request));

        ArgumentNullException.ThrowIfNull(importResult);

        ValidateUnityExport(unityRequest);

        if (string.IsNullOrWhiteSpace(importResult.MaterializedRoot))
            throw new NotSupportedException("Unity exports require a materialized intermediate package.");

        _logger.LogInformation("Starting JDI -> Unity conversion for '{MapName}' into '{OutputPath}'", importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song", unityRequest.OutputPath);
        IntermediateToUnityConverter converter = new(importResult.Package, importResult.MaterializedRoot, unityRequest, _logger);
        await converter.ConvertAsync();
        _logger.LogInformation("JDI -> Unity conversion completed for '{MapName}'", importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song");
    }

    private static string DetermineSongName(IntermediateSongPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.Metadata.MapName))
            return SanitizePathSegment(package.Metadata.MapName);
        return "UnitySong";
    }

    private static void PrepareMaterializedDirectory(string materializedRoot)
    {
        if (Directory.Exists(materializedRoot))
            Directory.Delete(materializedRoot, true);
        Directory.CreateDirectory(materializedRoot);
    }

    private static string BuildSuggestedOutputFolder(string outputPath, string songName)
    {
        string baseOutput = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), "JustDanceEditor", "Exports")
            : outputPath;
        return Path.Combine(baseOutput, songName);
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UnitySong";

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        string sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "UnitySong" : sanitized;
    }

    private static void ValidateUnityImport(UnityConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);

        if (!Directory.Exists(request.InputPath))
            throw new FileNotFoundException("Unity input folder not found.", request.InputPath);

        string songInfoPath = Path.Combine(request.InputPath, "SongInfo.json");
        if (!File.Exists(songInfoPath))
            throw new FileNotFoundException("SongInfo.json is required in the Unity input folder.", songInfoPath);
    }

    private static void ValidateUnityExport(UnityConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required for Unity exports.", nameof(request));

        if (request.ExportType == ExportType.OfflineCache && (!request.CacheNumber.HasValue || request.CacheNumber.Value == 0))
            throw new ArgumentException("A positive cache number is required for Unity offline cache exports.", nameof(request));

        if (request.ExportType is not ExportType.CustomServer and not ExportType.OfflineCache)
            throw new NotSupportedException($"Unity export type '{request.ExportType}' is not supported.");
    }
}