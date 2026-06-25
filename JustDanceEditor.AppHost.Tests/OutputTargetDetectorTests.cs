using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;

using Xunit;

namespace JustDanceEditor.AppHost.Tests;

public sealed class OutputTargetDetectorTests
{
    [Fact]
    public void Detect_SingleTargetFormat_ReturnsExactTarget()
    {
        using TempFolder temp = TempFolder.Create();
        string songPath = CreateDetectedSong(temp.Path);
        TestJdiFormat format = new("Single Format", songPath);
        ConversionTargetDefinition target = CreateTarget("single", "Single Format", "single-target");

        OutputTargetDetectionResult result = OutputTargetDetector.Detect(temp.Path, [format], [target]);

        Assert.Same(target, result.Target);
        Assert.Equal("Output format: Single Format", result.Message);
        Assert.Equal(1, result.DetectedSongCount);
    }

    [Fact]
    public void Detect_MultiTargetFormat_ReportsFormatWithoutTarget()
    {
        using TempFolder temp = TempFolder.Create();
        string songPath = CreateDetectedSong(temp.Path);
        TestJdiFormat format = new("Ambiguous Format", songPath);
        ConversionTargetDefinition firstTarget = CreateTarget("ambiguous", "Ambiguous Format", "first-target");
        ConversionTargetDefinition secondTarget = CreateTarget("ambiguous", "Ambiguous Format", "second-target");

        OutputTargetDetectionResult result = OutputTargetDetector.Detect(
            temp.Path,
            [format],
            [firstTarget, secondTarget]);

        Assert.Null(result.Target);
        Assert.Equal("Output format: Ambiguous Format", result.Message);
        Assert.Equal(1, result.DetectedSongCount);
    }

    private static string CreateDetectedSong(string outputPath)
    {
        string songPath = Path.Combine(outputPath, "detected-song");
        Directory.CreateDirectory(songPath);
        File.WriteAllText(Path.Combine(songPath, "marker.txt"), "detected");
        return songPath;
    }

    private static ConversionTargetDefinition CreateTarget(string formatCode, string formatName, string targetCode) =>
        new(
            FormatCode: formatCode,
            FormatName: formatName,
            TargetCode: targetCode,
            Platform: new PlatformDescriptor(targetCode, targetCode),
            Version: new TargetVersionDescriptor.Custom(targetCode, targetCode),
            DisplayName: targetCode);

    private sealed class TestJdiFormat(string displayName, string detectedPath) : IJdiFormat
    {
        public string DisplayName { get; } = displayName;
        public bool CanImport => true;
        public bool CanExport => false;

        public Task<JdiImportResult> ImportAsync(ConversionRequestBase request, CancellationToken cancellationToken = default) =>
            Task.FromException<JdiImportResult>(new NotSupportedException());

        public Task ExportAsync(JdiImportResult importResult, ConversionRequestBase request, CancellationToken cancellationToken = default) =>
            Task.FromException(new NotSupportedException());

        public bool Check(string inputPath) =>
            string.Equals(inputPath, detectedPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(inputPath, "marker.txt"));
    }

    private sealed class TempFolder : IDisposable
    {
        private TempFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempFolder Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jde-output-detector-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}