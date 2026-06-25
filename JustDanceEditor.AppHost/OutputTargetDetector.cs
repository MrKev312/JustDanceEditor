using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.AppHost;

public sealed record OutputTargetDetectionResult(
    ConversionTargetDefinition? Target,
    string Message,
    int DetectedSongCount = 0);

public static class OutputTargetDetector
{
    public static OutputTargetDetectionResult Detect(
        string outputPath,
        IEnumerable<IJdiFormat> formats,
        IEnumerable<IFormatConversionStrategy> strategies)
    {
        return Detect(
            outputPath,
            formats,
            ConversionTargetSelector.GetAvailableTargets(strategies));
    }

    public static OutputTargetDetectionResult Detect(
        string outputPath,
        IEnumerable<IJdiFormat> formats,
        IEnumerable<ConversionTargetDefinition> targets)
    {
        ConversionTargetDefinition[] targetList = [.. targets];

        if (string.IsNullOrWhiteSpace(outputPath))
            return Status("Unknown", null);

        if (!Directory.Exists(outputPath))
            return Status("Unknown", null);

        IJdiFormat[] importFormats = [.. formats.Where(format => format.CanImport)];
        if (importFormats.Length == 0)
            return Status("Unknown", null);

        OutputFolderScanResult scan;
        try
        {
            scan = ScanOutputFolder(outputPath, importFormats, targetList);
        }
        catch
        {
            return Status("Unknown", null);
        }

        return scan.Status switch
        {
            OutputScanStatus.Empty => Status("Empty", null, scan.DetectedSongCount),
            OutputScanStatus.Unknown => Status("Unknown", null, scan.DetectedSongCount),
            OutputScanStatus.Mixed => Status(FormatMixedStatus(scan.FormatNames), null, scan.DetectedSongCount),
            _ => Status(scan.FormatName ?? "Unknown", scan.Target, scan.DetectedSongCount)
        };
    }

    public static string FormatTargetLabel(ConversionTargetDefinition target)
    {
        return $"{target.Platform.DisplayName} / {target.DisplayName}";
    }

    private static OutputTargetDetectionResult Status(string status, ConversionTargetDefinition? target, int detectedSongCount = 0) =>
        new(target, $"Output format: {status}", detectedSongCount);

    private static OutputFolderScanResult ScanOutputFolder(
        string outputPath,
        IReadOnlyList<IJdiFormat> formats,
        IReadOnlyList<ConversionTargetDefinition> targets)
    {
        string[] childFolders = [.. Directory.GetDirectories(outputPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)];

        if (childFolders.Length == 0)
            return new(OutputScanStatus.Empty, null, null, 0, []);

        object gate = new();
        OutputSongDetection? firstSong = null;
        ConversionTargetDefinition? firstTarget = null;
        int detectedSongCount = 0;
        bool mixed = false;
        string[] mixedFormatNames = [];

        Parallel.ForEach(childFolders, (path, state) =>
        {
            if (Volatile.Read(ref mixed))
            {
                state.Stop();
                return;
            }

            OutputSongDetection? song = DetectSong(path, formats, targets);
            if (song is null)
                return;

            lock (gate)
            {
                if (mixed)
                {
                    state.Stop();
                    return;
                }

                if (firstSong is null)
                {
                    firstSong = song;
                    firstTarget = song.Target;
                    detectedSongCount++;
                    return;
                }

                if (!string.Equals(firstSong.DetectionKey, song.DetectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    mixed = true;
                    mixedFormatNames =
                    [
                        .. new[] { firstSong.Format.DisplayName, song.Format.DisplayName }
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    ];
                    detectedSongCount++;
                    state.Stop();
                    return;
                }

                detectedSongCount++;
            }
        });

        if (mixed)
            return new(OutputScanStatus.Mixed, null, null, detectedSongCount, mixedFormatNames);

        if (firstSong is null)
            return new(OutputScanStatus.Unknown, null, null, 0, []);

        return new(OutputScanStatus.Detected, firstSong.Format.DisplayName, firstTarget, detectedSongCount, [firstSong.Format.DisplayName]);
    }

    private static OutputSongDetection? DetectSong(
        string path,
        IReadOnlyList<IJdiFormat> formats,
        IReadOnlyList<ConversionTargetDefinition> targets)
    {
        IJdiFormat[] detected = [.. formats.Where(format => SafeCheck(format, path))];
        if (detected.Length == 0)
            return null;

        IJdiFormat format = detected.Length == 1
            ? detected[0]
            : detected.FirstOrDefault(format => !format.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)) ?? detected[0];

        ConversionTargetDefinition? target = ResolveTarget(format, targets);
        string detectionKey = target is null
            ? $"format:{format.DisplayName}"
            : $"target:{GetTargetKey(target)}";

        return new(path, format, target, detectionKey);
    }

    private static bool SafeCheck(IJdiFormat format, string path)
    {
        try
        {
            return format.Check(path);
        }
        catch
        {
            return false;
        }
    }

    private static ConversionTargetDefinition? ResolveTarget(IJdiFormat format, IReadOnlyList<ConversionTargetDefinition> targets)
    {
        ConversionTargetDefinition[] formatTargets =
        [
            .. targets.Where(target => target.FormatName.Equals(format.DisplayName, StringComparison.OrdinalIgnoreCase))
        ];

        return formatTargets.Length == 1
            ? formatTargets[0]
            : null;
    }

    private static string FormatMixedStatus(IReadOnlyList<string> formatNames)
    {
        return formatNames.Count == 0
            ? "Mixed"
            : $"Mixed ({string.Join(", ", formatNames)})";
    }

    private static string GetTargetKey(ConversionTargetDefinition target) =>
        $"{target.FormatCode}:{target.TargetCode}";

    private sealed record OutputFolderScanResult(
        OutputScanStatus Status,
        string? FormatName,
        ConversionTargetDefinition? Target,
        int DetectedSongCount,
        IReadOnlyList<string> FormatNames);

    private sealed record OutputSongDetection(
        string Path,
        IJdiFormat Format,
        ConversionTargetDefinition? Target,
        string DetectionKey);

    private enum OutputScanStatus
    {
        Empty,
        Unknown,
        Detected,
        Mixed
    }
}