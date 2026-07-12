using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace JustDanceEditor.Editor.Views.Timeline;

/// <summary>
/// Aggregates timeline rendering timings. Reporting is throttled and file writes run
/// off the UI thread so diagnostics remain useful without becoming the bottleneck.
/// </summary>
internal static class TimelineRenderDiagnostics
{
    public const bool Enabled = true;
    private const double ReportIntervalSeconds = 2;
    private static readonly Lock Gate = new();
    private static readonly Lock FileGate = new();
    private static readonly Dictionary<string, Metric> Metrics = [];
    private static long _nextReportTimestamp = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * ReportIntervalSeconds);
    public static string LogPath { get; } = GetLogPath();

    static TimelineRenderDiagnostics()
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogPath);
            if (directory != null)
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                LogPath,
                $"Timeline render diagnostics started {DateTimeOffset.Now:O} (PID {Environment.ProcessId}){Environment.NewLine}");
            Trace.WriteLine($"[Timeline render] Logging to '{LogPath}'.");
        }
        catch (Exception exception)
        {
            Trace.TraceWarning("Failed to initialize timeline render log '{0}': {1}", LogPath, exception);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void RecordDuration(string name, long startTimestamp, int units = 0)
    {
        long now = Stopwatch.GetTimestamp();
        Record(name, now - startTimestamp, units, hasDuration: true, now);
    }

    public static void RecordCount(string name, int units = 1)
    {
        long now = Stopwatch.GetTimestamp();
        Record(name, 0, units, hasDuration: false, now);
    }

    private static void Record(string name, long elapsedTicks, int units, bool hasDuration, long now)
    {
        string? report = null;
        lock (Gate)
        {
            if (!Metrics.TryGetValue(name, out Metric? metric))
            {
                metric = new Metric(hasDuration);
                Metrics[name] = metric;
            }

            metric.Count++;
            metric.Units += units;
            metric.TotalTicks += elapsedTicks;
            metric.MaxTicks = Math.Max(metric.MaxTicks, elapsedTicks);

            if (now >= _nextReportTimestamp)
            {
                report = BuildReport();
                Metrics.Clear();
                _nextReportTimestamp = now + (long)(Stopwatch.Frequency * ReportIntervalSeconds);
            }
        }

        if (report != null)
        {
            Trace.WriteLine(report);
            QueueFileWrite(report);
        }
    }

    private static string GetLogPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        return Path.Combine(localAppData, "JustDanceEditor", "Logs", "timeline-render.log");
    }

    private static void QueueFileWrite(string report)
    {
        ThreadPool.QueueUserWorkItem(static state =>
        {
            try
            {
                lock (FileGate)
                    File.AppendAllText(LogPath, (string)state! + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("Failed to write timeline render diagnostics: {0}", exception);
            }
        }, report);
    }

    private static string BuildReport()
    {
        StringBuilder report = new("[Timeline render]");
        foreach ((string name, Metric metric) in Metrics.OrderBy(static pair => pair.Key))
        {
            report.Append(' ').Append(name).Append(": ").Append(metric.Count).Append('x');
            if (metric.HasDuration)
            {
                double totalMs = metric.TotalTicks * 1000.0 / Stopwatch.Frequency;
                double maxMs = metric.MaxTicks * 1000.0 / Stopwatch.Frequency;
                report.Append(", total=").Append(totalMs.ToString("F2"))
                    .Append("ms, avg=").Append((totalMs / metric.Count).ToString("F3"))
                    .Append("ms, max=").Append(maxMs.ToString("F3")).Append("ms");
            }

            if (metric.Units != 0)
                report.Append(", units=").Append(metric.Units);
            report.Append(" |");
        }

        return report.ToString();
    }

    private sealed class Metric(bool hasDuration)
    {
        public bool HasDuration { get; } = hasDuration;
        public int Count { get; set; }
        public int Units { get; set; }
        public long TotalTicks { get; set; }
        public long MaxTicks { get; set; }
    }
}
