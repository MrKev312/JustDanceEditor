using JustDanceEditor.Converter.Converters;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Converter.Formats;

internal sealed class UnityJdiFormat : IJdiFormat
{
	private readonly IRequestValidator _requestValidator;

	public UnityJdiFormat(IRequestValidator requestValidator)
	{
		_requestValidator = requestValidator;
	}

	public string DisplayName => "Unity";
	public JdiFormatKind Kind => JdiFormatKind.Unity;
	public bool CanImport => true;
	public bool CanExport => true;

	public Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		IntermediateSongPackage package = UnityServerIntermediateBuilder.FromServerExport(request.InputPath);
		JdiConversionHelpers.EnsureSongName(request, package, allowFallbackToMetadata: true);
		JdiImportResult result = new(package, JdiFormatKind.Unity);
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

		IntermediateToUnityConverter converter = new(importResult.Package, importResult.MaterializedRoot, request, _requestValidator);
		await converter.ConvertAsync();
	}
}