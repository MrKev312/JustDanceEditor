using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Converters;

namespace JustDanceEditor.Formats.Unity.Formats;

public sealed class UnityJdiFormat(IRequestValidator requestValidator) : IJdiFormat
{
    public UnityJdiFormat() : this(new RequestValidator()) { }

    public string DisplayName => "Unity";
    public bool CanImport => true;
    public bool CanExport => true;

    public Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);

        IntermediateSongPackage package = UnityServerIntermediateBuilder.FromServerExport(request.InputPath);
        JdiConversionHelpers.EnsureSongName(request, package, allowFallbackToMetadata: true);

        string songName = DetermineSongName(request, package);
        string stagingRoot = CreateStagingRoot(songName);
        PrepareStagingDirectory(stagingRoot);

        UnityAssetMaterializer.Materialize(package, request.InputPath, stagingRoot);
        IntermediatePackageSerializer.WriteToFolder(package, stagingRoot);

        string suggestedOutput = BuildSuggestedOutputFolder(request, songName);

        JdiImportResult result = new(
            package,
            "Unity",
            stagingRoot,
            MaterializedRootIsTemporary: true,
            SuggestedOutputFolder: suggestedOutput);

        return Task.FromResult(result);
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importResult);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExportType != ExportType.CustomServer)
            throw new NotSupportedException("Unity exports currently support only the Custom Server folder layout.");

        if (string.IsNullOrWhiteSpace(importResult.MaterializedRoot))
            throw new NotSupportedException("Unity exports require a materialized intermediate package.");

        if (!string.IsNullOrWhiteSpace(importResult.Package.Metadata.MapName))
            request.SongName = importResult.Package.Metadata.MapName;

        IntermediateToUnityConverter converter = new(importResult.Package, importResult.MaterializedRoot, request, requestValidator);
        await converter.ConvertAsync();
    }

        private static string DetermineSongName(ConversionRequest request, IntermediateSongPackage package)
        {
            if (!string.IsNullOrWhiteSpace(package.Metadata.MapName))
                return SanitizePathSegment(package.Metadata.MapName);
            if (!string.IsNullOrWhiteSpace(request.SongName))
                return SanitizePathSegment(request.SongName);
            return "UnitySong";
        }

        private static string CreateStagingRoot(string songName)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", songName, "IntermediateStaging");
            return Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        }

        private static void PrepareStagingDirectory(string stagingRoot)
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, true);
            Directory.CreateDirectory(stagingRoot);
        }

        private static string BuildSuggestedOutputFolder(ConversionRequest request, string songName)
        {
            string baseOutput = string.IsNullOrWhiteSpace(request.OutputPath)
                ? Path.Combine(Path.GetTempPath(), "JustDanceEditor", "Exports")
                : request.OutputPath;
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
}
