using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Recordings;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed class UbiArtRecordingExporter(ILogger logger)
{
    public async Task ExportAsync(UbiArtExportPlan plan)
    {
        if (plan.Platform != UbiArtPlatform.Uncooked || string.IsNullOrWhiteSpace(plan.MaterializedRoot))
            return;

        IntermediateSongPackage package = plan.Package;
        string materializedRoot = plan.MaterializedRoot;
        string mapWorldBase = plan.MapWorldBase;
        ExportContext context = plan.Context;
        JsonMotionRecordingRepository repository = new();
        MotionTrainingSelectionDocument trainingSelection = await new JsonMotionTrainingSelectionRepository()
            .LoadAsync(materializedRoot);
        IReadOnlyList<string> recordingPaths = repository.ListRecordingFiles(materializedRoot);
        if (recordingPaths.Count == 0)
            return;

        string timelineFolder = context.IO.Combine(context.OutputFolder, mapWorldBase, "timeline");
        string recordingFolder = context.IO.Combine(timelineFolder, "Recording");
        context.IO.CreateDirectory(recordingFolder);

        Dictionary<int, List<(string FileName, MotionRecordingDocument Recording)>> byCoach = [];
        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        RecMotionFormat recFormat = context.EngineVersion is UbiArtEngineVersion.JD2014 or UbiArtEngineVersion.JD2015
            ? RecMotionFormat.Wii
            : RecMotionFormat.Modern;
        foreach (string recordingPath in recordingPaths)
        {
            MotionRecordingDocument recording = await repository.LoadAsync(recordingPath).ConfigureAwait(false);
            if (recording.Samples.Count == 0)
                continue;

            string fileName = CreateRecFileName(recording, package.Metadata.MapName, usedFileNames);
            string destination = context.IO.Combine(recordingFolder, fileName);
            await using (FileStream output = File.Create(destination))
                RecMotionRecordingCodec.Write(output, recording, recFormat, package.Metadata.MapName);

            if (!byCoach.TryGetValue(recording.CoachId, out List<(string, MotionRecordingDocument)>? coachRecordings))
            {
                coachRecordings = [];
                byCoach.Add(recording.CoachId, coachRecordings);
            }
            coachRecordings.Add((fileName, recording));
        }

        if (byCoach.Count == 0)
            return;

        string snippingFolder = context.IO.Combine(timelineFolder, "Snipping");
        context.IO.CreateDirectory(snippingFolder);
        foreach ((int coachId, List<(string FileName, MotionRecordingDocument Recording)> recordings) in byCoach)
        {
            IReadOnlyList<UbiArtSnipSelection> columns = BuildSelectionColumns(package, coachId);
            if (columns.Count == 0)
                continue;

            string fileName = $"Moves{coachId + 1}_{SanitizeFilePart(package.Metadata.MapName)}_validation.csv";
            await using FileStream output = File.Create(context.IO.Combine(snippingFolder, fileName));
            UbiArtSnippingTable.Write(
                output,
                recordings.Select(item =>
                    (item.FileName, BuildRecordingSelections(item.Recording, columns, trainingSelection))).ToArray(),
                columns);
        }

        logger.LogInformation("Exported {RecordingCount} recording(s) to uncooked REC files.", byCoach.Values.Sum(static recordings => recordings.Count));
    }

    private static IReadOnlyList<UbiArtSnipSelection> BuildSelectionColumns(
        IntermediateSongPackage package,
        int coachId)
    {
        MoveTimeline? timeline = package.CoachTimelines.FirstOrDefault(item => item.CoachId == coachId);
        if (timeline == null)
            return [];

        List<UbiArtSnipSelection> result = [];
        Dictionary<string, int> occurrences = new(StringComparer.OrdinalIgnoreCase);
        foreach (MoveClip clip in timeline.Clips
                     .Where(static clip => !string.IsNullOrWhiteSpace(clip.MoveId))
                     .OrderBy(static clip => clip.StartTime))
        {
            int occurrence = occurrences.GetValueOrDefault(clip.MoveId);
            occurrences[clip.MoveId] = occurrence + 1;
            result.Add(new UbiArtSnipSelection(
                clip.MoveId,
                clip.StartTime / 24.0,
                IsIncluded: true,
                clip.Id,
                occurrence));
        }

        return result;
    }

    private static IReadOnlyList<UbiArtSnipSelection> BuildRecordingSelections(
        MotionRecordingDocument recording,
        IReadOnlyList<UbiArtSnipSelection> columns,
        MotionTrainingSelectionDocument trainingSelection)
        =>
        [
            .. columns.Select(column => column with
            {
                IsIncluded = !trainingSelection.IsExcluded(
                    recording.RecordingId,
                    recording.CoachId,
                    column.TimelineClipId,
                    column.MoveId,
                    column.MoveOccurrence)
            })
        ];

    private static string CreateRecFileName(
        MotionRecordingDocument recording,
        string mapName,
        HashSet<string> usedFileNames)
    {
        string candidate = $"Moves{recording.CoachId + 1}_{SanitizeFilePart(mapName)}_JDI_{recording.StartedAtUtc:yyyy-MM-dd_HH-mm-ss}.rec";

        string stem = Path.GetFileNameWithoutExtension(candidate);
        string extension = Path.GetExtension(candidate);
        int suffix = 2;
        while (!usedFileNames.Add(candidate))
            candidate = $"{stem}_{suffix++}{extension}";
        return candidate;
    }

    private static string SanitizeFilePart(string value)
    {
        HashSet<char> invalid = [.. Path.GetInvalidFileNameChars()];
        string sanitized = new(value.Select(character => invalid.Contains(character) || character is ';' or '\r' or '\n' ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "recording" : sanitized;
    }
}
