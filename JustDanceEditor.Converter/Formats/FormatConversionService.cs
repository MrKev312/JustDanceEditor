using JustDanceEditor.Converter.Converters;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Intermediate;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Formats.Intermediate;
using JustDanceEditor.Formats.Intermediate.Metadata;
using JustDanceEditor.Formats.Intermediate.Serialization;

namespace JustDanceEditor.Converter.Formats;

public class FormatConversionService
{
    private readonly IRequestValidator _requestValidator;
    private readonly ISongDataLoader _songDataLoader;

    public FormatConversionService()
    {
        _requestValidator = new RequestValidator();
        _songDataLoader = new SongDataLoader();
    }

    public async Task ConvertAsync(FormatKind source, FormatKind target, ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (source == target)
            return;

        IntermediateImportResult importResult = source switch
        {
            FormatKind.UbiArt => await ImportFromUbiArtAsync(request),
            FormatKind.Unity => ImportFromUnity(request),
            FormatKind.JDI => ImportFromIntermediate(request),
            _ => throw new NotImplementedException($"Conversion from {source} is not implemented yet.")
        };

        await (target switch
        {
            FormatKind.JDI => ExportToIntermediateAsync(importResult, request, source),
            FormatKind.Unity => ExportToUnityAsync(importResult, request),
            _ => throw new NotImplementedException($"Conversion to {target} is not implemented yet.")
        });
    }

    private async Task<IntermediateImportResult> ImportFromUbiArtAsync(ConversionRequest request)
    {
        FileSystem fileSystem = new(request);
        ConversionContext context = new(request, fileSystem);

        _requestValidator.ValidateTemplateFolder(request.TemplatePath);
        _requestValidator.ValidateConversionRequest(request);

        context.SongData = _songDataLoader.LoadSongData(request, fileSystem);
        context.FileSystem.UpdateSongName(context.SongData.Name);
        context.IntermediatePackage = IntermediatePackageBuilder.FromUbiArt(context);
        context.UnityData = UnityExportDataBuilder.Create(context.IntermediatePackage);

        return new IntermediateImportResult(context.IntermediatePackage, context, context.FileSystem.OutputFolders.IntermediateFolder);
    }

    private static IntermediateImportResult ImportFromUnity(ConversionRequest request)
    {
        IntermediateSongPackage package = UnityServerIntermediateBuilder.FromServerExport(request.InputPath);
        EnsureSongName(request, package, allowFallbackToMetadata: true);
        return new IntermediateImportResult(package, null, null);
    }

    private static IntermediateImportResult ImportFromIntermediate(ConversionRequest request)
    {
        string folder = LocateIntermediateFolder(request.InputPath);
        IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(folder);
        EnsureSongName(request, package);
        return new IntermediateImportResult(package, null, folder);
    }

    private async Task ExportToIntermediateAsync(IntermediateImportResult importResult, ConversionRequest request, FormatKind sourceFormat)
    {
        string targetFolder = importResult.Context?.FileSystem.OutputFolders.OutputFolder
            ?? BuildSongOutputFolder(request.OutputPath, importResult.Package.Metadata);

        Directory.CreateDirectory(targetFolder);

        if (sourceFormat == FormatKind.UbiArt && importResult.Context != null)
        {
            await IntermediateAssetWriter.PopulateFromUbiArtAsync(importResult.Context, importResult.Package, targetFolder);
        }
        else if (sourceFormat == FormatKind.Unity)
        {
            UnityAssetMaterializer.Materialize(importResult.Package, request.InputPath, targetFolder);
        }

        IntermediatePackageSerializer.WriteToFolder(importResult.Package, targetFolder);
    }

    private async Task ExportToUnityAsync(IntermediateImportResult importResult, ConversionRequest request)
    {
        if (request.ExportType != ExportType.CustomServer)
            throw new NotSupportedException("Unity exports currently support only the Custom Server folder layout.");

        if (importResult.Context != null)
        {
            importResult.Context.IntermediatePackage = importResult.Package;
            UbiArtToUnityConverter converter = new(importResult.Context, _requestValidator, _songDataLoader);
            await converter.ConvertWithExistingContextAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(importResult.PackageRoot))
            throw new NotSupportedException("Intermediate packages converted from Unity snapshots do not contain enough data to rebuild Unity bundles.");

        if (!string.IsNullOrWhiteSpace(importResult.Package.Metadata.MapName))
            request.SongName = importResult.Package.Metadata.MapName;

        IntermediateToUnityConverter intermediateConverter = new(importResult.Package, importResult.PackageRoot, request, _requestValidator);
        await intermediateConverter.ConvertAsync();
    }

    private static string LocateIntermediateFolder(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be empty.", nameof(inputPath));

        if (IsIntermediateFolder(inputPath))
            return inputPath;

        string nested = Path.Combine(inputPath, "Intermediate");
        if (IsIntermediateFolder(nested))
            return nested;

        try
        {
            foreach (string manifest in Directory.EnumerateFiles(inputPath, "manifest.json", SearchOption.AllDirectories))
            {
                string? folder = Path.GetDirectoryName(manifest);
                if (folder != null && IsIntermediateFolder(folder))
                    return folder;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore folders we cannot read and fall through to the error below.
        }

        throw new DirectoryNotFoundException($"Could not find an intermediate package under '{inputPath}'. Ensure the folder contains a manifest.json generated by the converter.");
    }

    private static bool IsIntermediateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;

        string manifestPath = Path.Combine(folder, "manifest.json");
        return File.Exists(manifestPath);
    }

    private static void EnsureSongName(ConversionRequest request, IntermediateSongPackage package, bool allowFallbackToMetadata = false)
    {
        if (!string.IsNullOrWhiteSpace(request.SongName))
            return;

        if (!string.IsNullOrWhiteSpace(package.Metadata.MapName))
        {
            request.SongName = package.Metadata.MapName;
            return;
        }

        if (allowFallbackToMetadata)
            request.SongName = package.Metadata.Title;
    }

    private static string BuildSongOutputFolder(string baseOutput, IntermediateMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseOutput);
        ArgumentNullException.ThrowIfNull(metadata);

        string? codeName = string.IsNullOrWhiteSpace(metadata.MapName) ? metadata.Title : metadata.MapName;
        if (string.IsNullOrWhiteSpace(codeName))
            codeName = "Song";

        string folderName = SanitizeFolderName(codeName);
        return Path.Combine(baseOutput, folderName);
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
