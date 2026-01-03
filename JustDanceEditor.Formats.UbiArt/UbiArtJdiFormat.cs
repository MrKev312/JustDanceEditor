using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Intermediate;
using JustDanceEditor.Formats.UbiArt.Services;
using JustDanceEditor.Logging;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtJdiFormat(ISongDataLoader songDataLoader) : IJdiFormat
{
    public UbiArtJdiFormat() : this(new SongDataLoader()) { }

    public string DisplayName => "UbiArt";
    public bool CanImport => true;
    public bool CanExport => true;

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt import expects a UbiArtConversionRequest.", nameof(request));

        ValidateUbiArtImport(ubiRequest);

        FileSystem fileSystem = new(ubiRequest);

        // Log song name and platform here (moved from FileSystem internals)
        if (!string.IsNullOrWhiteSpace(fileSystem.SongName))
            Logger.Log($"Song name: {fileSystem.SongName}", LogLevel.Important);

        if (!fileSystem.PlatformType.Equals("nx", StringComparison.CurrentCultureIgnoreCase))
            Logger.Log($"Platform: {fileSystem.PlatformType}, which is not officially supported. The conversion might not work as expected.", LogLevel.Warning);
        else
            Logger.Log($"Platform: {fileSystem.PlatformType}");

        ConversionContext context = new(ubiRequest, fileSystem)
        {
            SongData = songDataLoader.LoadSongData(ubiRequest, fileSystem)
        };
        context.FileSystem.UpdateSongName(context.SongData.Name);
        context.IntermediatePackage = IntermediatePackageBuilder.FromUbiArt(context);
        string outputFolder = Path.Combine(ubiRequest.OutputPath, context.SongData.Name);
        PrepareOutputDirectory(outputFolder);

        await IntermediateAssetWriter.PopulateFromUbiArtAsync(context, context.IntermediatePackage, outputFolder);
        IntermediatePackageSerializer.WriteToFolder(context.IntermediatePackage, outputFolder);

        return new JdiImportResult(
            context.IntermediatePackage,
            "UbiArt",
            outputFolder,
            MaterializedRootIsTemporary: outputFolder.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            SuggestedOutputFolder: outputFolder);
    }

    public async Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt export expects a UbiArtConversionRequest.", nameof(request));

        if (ubiRequest.Type != UbiArtType.Uncooked)
            throw new NotSupportedException("Only Uncooked export is supported for now.");

        string outputFolder = ubiRequest.OutputPath;
        if (!string.IsNullOrEmpty(ubiRequest.SongName))
        {
            outputFolder = Path.Combine(outputFolder, ubiRequest.SongName);
        }
        else if (importResult.Package.Metadata.MapName != null)
        {
            outputFolder = Path.Combine(outputFolder, importResult.Package.Metadata.MapName);
        }

        PrepareOutputDirectory(outputFolder);

        await UbiArtAssetWriter.ExportToUncookedAsync(importResult.Package, importResult.MaterializedRoot, outputFolder);
    }

    private static void PrepareOutputDirectory(string targetFolder)
    {
        if (Directory.Exists(targetFolder))
            Directory.Delete(targetFolder, true);
        Directory.CreateDirectory(targetFolder);
    }

    public bool Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            bool isCooked = Directory.Exists(Path.Combine(path, "cache", "itf_cooked"));
            UbiArtConversionRequest req = new(path, Path.GetTempPath(), null) { Type = isCooked ? UbiArtType.Cooked : UbiArtType.Uncooked };
            FileSystem fs = new(req);

            // Use FileSystem.GetFilePath like SongDataLoader does to correctly find songdesc.tpl
            string songDescRelativePath = Path.Combine(fs.InputFolders.MapWorldFolder, "songdesc.tpl");
            bool hasSongDesc = fs.GetFilePath(songDescRelativePath, out CookedFile? songDescCooked);

            bool hasJddb = File.Exists(Path.Combine(fs.InputFolders.InputFolder, "jddb.json"))
                || File.Exists(Path.Combine(fs.InputFolders.InputFolder, "..", "jddb.json"));

            if (!hasSongDesc && !hasJddb)
                return false;

            // Attempt to load song data to determine engine version and report platform
            try
            {
                SongDesc sd = songDataLoader.LoadSongDesc(req, fs);
                uint engine = sd.COMPONENTS[0].JDVersion;
                uint original = sd.COMPONENTS[0].OriginalJDVersion;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Detected UbiArt platform: {fs.PlatformType}, engine version: {engine}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Logger.Log($"UbiArt detection: failed to read SongDesc for engine/version: {ex.Message}", LogLevel.Warning);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"UbiArt detection failed: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    private static void ValidateUbiArtImport(UbiArtConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputPath))
            throw new ArgumentException("Input path is required for UbiArt imports.", nameof(request.InputPath));
        if (!Directory.Exists(request.InputPath))
            throw new FileNotFoundException("Input folder not found", request.InputPath);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required for UbiArt imports.", nameof(request.OutputPath));

        // Use filesystem for this
        new FileSystem(request).GetFilePath($"world/maps/{request.SongName}/songdesc.tpl", out CookedFile? songDesc);
        bool hasSongDesc = songDesc != null;
        bool hasJddb = Directory.EnumerateFiles(request.InputPath, "jddb.json", SearchOption.AllDirectories).Any();

        if (!hasSongDesc && !hasJddb)
            throw new FileNotFoundException("songdesc.tpl or jddb.json is required for UbiArt imports.");
    }
}