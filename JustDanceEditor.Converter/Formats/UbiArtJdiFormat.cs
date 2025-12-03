using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Intermediate;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Converter.Formats;

internal sealed partial class UbiArtJdiFormat : IJdiFormat
{
    private readonly IRequestValidator _requestValidator;
    private readonly ISongDataLoader _songDataLoader;

    public UbiArtJdiFormat(IRequestValidator requestValidator, ISongDataLoader songDataLoader)
    {
        _requestValidator = requestValidator;
        _songDataLoader = songDataLoader;
    }

    public string DisplayName => "UbiArt";
    public JdiFormatKind Kind => JdiFormatKind.UbiArt;
    public bool CanImport => true;
    public bool CanExport => false;

    public async Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FileSystem fileSystem = new(request);
        ConversionContext context = new(request, fileSystem);

        _requestValidator.ValidateTemplateFolder(request.TemplatePath);
        _requestValidator.ValidateConversionRequest(request);

        context.SongData = _songDataLoader.LoadSongData(request, fileSystem);
        context.FileSystem.UpdateSongName(context.SongData.Name);
        context.IntermediatePackage = IntermediatePackageBuilder.FromUbiArt(context);
        InitializeUnityData(context);

        string stagingRoot = await MaterializeUbiArtIntermediateAsync(context, context.IntermediatePackage);

        return new JdiImportResult(
            context.IntermediatePackage,
            JdiFormatKind.UbiArt,
            stagingRoot,
            MaterializedRootIsTemporary: true,
            SuggestedOutputFolder: context.FileSystem.OutputFolders.OutputFolder);
    }

    public Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Exporting to UbiArt is not supported.");

    private static async Task<string> MaterializeUbiArtIntermediateAsync(ConversionContext context, IntermediateSongPackage package)
    {
        string stagingRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "IntermediateStaging", Guid.NewGuid().ToString("N"));
        JdiConversionHelpers.TryDeleteDirectory(stagingRoot);
        Directory.CreateDirectory(stagingRoot);

        await IntermediateAssetWriter.PopulateFromUbiArtAsync(context, package, stagingRoot);
        IntermediatePackageSerializer.WriteToFolder(package, stagingRoot);
        return stagingRoot;
    }
}
