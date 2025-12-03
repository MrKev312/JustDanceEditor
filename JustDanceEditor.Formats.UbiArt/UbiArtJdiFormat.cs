using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Intermediate;
using JustDanceEditor.Formats.UbiArt.Services;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class UbiArtJdiFormat(IRequestValidator requestValidator, ISongDataLoader songDataLoader) : IJdiFormat
{
    public UbiArtJdiFormat() : this(new RequestValidator(), new SongDataLoader()) { }

    public string DisplayName => "UbiArt";
    public bool CanImport => true;
    public bool CanExport => false;

    public async Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FileSystem fileSystem = new(request);
        ConversionContext context = new(request, fileSystem);

        requestValidator.ValidateConversionRequest(request);

        context.SongData = songDataLoader.LoadSongData(request, fileSystem);
        context.FileSystem.UpdateSongName(context.SongData.Name);
        context.IntermediatePackage = IntermediatePackageBuilder.FromUbiArt(context);

        string stagingRoot = await MaterializeUbiArtIntermediateAsync(context, context.IntermediatePackage);

        return new JdiImportResult(
            context.IntermediatePackage,
            "UbiArt",
            stagingRoot,
            MaterializedRootIsTemporary: true,
            SuggestedOutputFolder: context.FileSystem.OutputFolders.OutputFolder);
    }

    public Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Exporting to UbiArt is not supported.");

    private static async Task<string> MaterializeUbiArtIntermediateAsync(ConversionContext context, IntermediateSongPackage package)
    {
        string stagingRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "IntermediateStaging", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(stagingRoot))
            Directory.Delete(stagingRoot, true);
        Directory.CreateDirectory(stagingRoot);

        await IntermediateAssetWriter.PopulateFromUbiArtAsync(context, package, stagingRoot);
        IntermediatePackageSerializer.WriteToFolder(package, stagingRoot);
        return stagingRoot;
    }
}
