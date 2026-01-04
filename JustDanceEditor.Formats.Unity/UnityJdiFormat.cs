using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.Unity.Converters;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnityJdiFormat(Func<string, IntermediateSongPackage> serverBuilder, Services.IUnityAssetMaterializer assetMaterializer, Microsoft.Extensions.Logging.ILogger<UnityJdiFormat> logger) : IJdiFormat
{
    private readonly Func<string, IntermediateSongPackage> _serverBuilder = serverBuilder;
    private readonly Services.IUnityAssetMaterializer _assetMaterializer = assetMaterializer;
    private readonly Microsoft.Extensions.Logging.ILogger<UnityJdiFormat> _logger = logger;

    public string DisplayName => "Unity";
    public bool CanImport => true;
    public bool CanExport => true;

    public Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UnityConversionRequest unityRequest)
            throw new ArgumentException("Unity import expects a UnityConversionRequest.", nameof(request));

        ValidateUnityImport(unityRequest);

        IntermediateSongPackage package = _serverBuilder(unityRequest.InputPath);

        string songName = DetermineSongName(package);
        string suggestedOutput = BuildSuggestedOutputFolder(unityRequest.OutputPath, songName);
        PrepareMaterializedDirectory(suggestedOutput);

        _assetMaterializer.Materialize(package, unityRequest.InputPath, suggestedOutput);
        IntermediatePackageSerializer.WriteToFolder(package, suggestedOutput);

        JdiImportResult result = new(
            package,
            "Unity",
            suggestedOutput,
            MaterializedRootIsTemporary: false,
            SuggestedOutputFolder: suggestedOutput);

        return Task.FromResult(result);
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

        IntermediateToUnityConverter converter = new(importResult.Package, importResult.MaterializedRoot, unityRequest, _logger);
        await converter.ConvertAsync();
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

        if (request.ExportType != ExportType.CustomServer)
            throw new NotSupportedException("Unity exports currently support only the Custom Server folder layout.");

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required for Unity exports.", nameof(request.OutputPath));

        ValidateTemplateFolder(request.TemplatePath);
    }

    private static void ValidateTemplateFolder(string templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
            throw new ArgumentException("Template path is required for Unity exports.", nameof(templatePath));

        if (!Directory.Exists(templatePath))
            throw new DirectoryNotFoundException($"Template path '{templatePath}' does not exist.");

        string[] foldersToValidate = [
            Path.Combine(templatePath, "Cover"),
            Path.Combine(templatePath, "MapPackage"),
            Path.Combine(templatePath, "CoachesLarge"),
            Path.Combine(templatePath, "CoachesSmall"),
            Path.Combine(templatePath, "SongTitleLogo")
        ];

        foreach (string folder in foldersToValidate)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"The template subfolder {folder} is missing. Please ensure the template structure is correct.");

            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                throw new FileNotFoundException($"The template folder {folder} is empty. Please put a template file in the folder.");
        }
    }
}