using JustDanceEditor.Converter.Core;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Converter.Formats;

internal sealed record IntermediateImportResult(
	IntermediateSongPackage Package,
	ConversionContext? Context,
	string? PackageRoot,
	bool PackageRootIsTemporary = false);
