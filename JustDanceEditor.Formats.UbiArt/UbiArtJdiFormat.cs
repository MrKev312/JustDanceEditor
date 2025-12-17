using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Intermediate;
using JustDanceEditor.Formats.UbiArt.Services;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtJdiFormat(ISongDataLoader songDataLoader) : IJdiFormat
{
    public UbiArtJdiFormat() : this(new SongDataLoader()) { }

    public string DisplayName => "UbiArt";
    public bool CanImport => true;
    public bool CanExport => false;

    public async Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is not UbiArtConversionRequest ubiRequest)
            throw new ArgumentException("UbiArt import expects a UbiArtConversionRequest.", nameof(request));

        ValidateUbiArtImport(ubiRequest);

        FileSystem fileSystem = new(ubiRequest);
        ConversionContext context = new(ubiRequest, fileSystem);

        context.SongData = songDataLoader.LoadSongData(ubiRequest, fileSystem);
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
            MaterializedRootIsTemporary: false,
            SuggestedOutputFolder: outputFolder);
    }

    public Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Exporting to UbiArt is not supported.");

    private static void PrepareOutputDirectory(string targetFolder)
    {
        if (Directory.Exists(targetFolder))
            Directory.Delete(targetFolder, true);
        Directory.CreateDirectory(targetFolder);
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

        bool hasSongDesc = Directory.EnumerateFiles(request.InputPath, "songdesc.tpl", SearchOption.AllDirectories).Any();
        bool hasJddb = Directory.EnumerateFiles(request.InputPath, "jddb.json", SearchOption.AllDirectories).Any();

        if (!hasSongDesc && !hasJddb)
            throw new FileNotFoundException("songdesc.tpl or jddb.json is required for UbiArt imports.");
    }
}