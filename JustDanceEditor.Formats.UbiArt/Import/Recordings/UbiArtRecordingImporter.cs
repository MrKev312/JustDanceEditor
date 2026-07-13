using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Recordings;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Recordings;

internal static class UbiArtRecordingImporter
{
    public static async Task ImportAsync(
        ConversionContext context,
        string packageRoot,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (context.FileSystem.VersionProfile.Platform != UbiArtPlatform.Uncooked)
            return;

        string timelineFolder = context.FileSystem.VersionProfile.Layout.GetTimelineFolder(
            string.Empty,
            context.FileSystem.ContentSongName,
            UbiArtPlatform.Uncooked,
            context.FileSystem.VersionProfile.EngineVersion);
        CookedFile[] recordingFiles = FindFiles(context.FileSystem, timelineFolder, "Recording", "*.rec");
        if (recordingFiles.Length == 0)
            return;

        IReadOnlyList<UbiArtSnippingRow> snippingRows = ReadSnippingRows(context.FileSystem, timelineFolder, logger);
        JsonMotionRecordingRepository repository = new();
        JsonMotionTrainingSelectionRepository selectionRepository = new();
        MotionTrainingSelectionDocument trainingSelection = await selectionRepository.LoadAsync(packageRoot, cancellationToken);
        bool selectionChanged = false;
        int importedCount = 0;

        foreach (CookedFile recordingFile in recordingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(recordingFile.RelativePath);
            UbiArtSnippingRow? snipping = FindSnippingRow(snippingRows, fileName);
            int firstCoachId = RecMotionRecordingCodec.InferCoachId(fileName) ?? snipping?.CoachId ?? 0;

            try
            {
                using Stream stream = context.FileSystem.GetFileStream(recordingFile);
                IReadOnlyList<MotionRecordingDocument> recordings = RecMotionRecordingCodec.Read(
                    stream,
                    firstCoachId,
                    fileName,
                    context.IntermediatePackage.Metadata.SongID.ToString("D"));
                foreach (MotionRecordingDocument recording in recordings)
                {
                    if (string.IsNullOrWhiteSpace(recording.MapName))
                        recording.MapName = context.IntermediatePackage.Metadata.MapName;
                    await repository.SaveAsync(packageRoot, recording, cancellationToken).ConfigureAwait(false);
                    if (snipping != null)
                        selectionChanged |= ImportExclusions(context.IntermediatePackage, recording, snipping.Selections, trainingSelection);
                    importedCount++;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException or ArgumentException)
            {
                logger.LogWarning(ex, "Failed to import REC recording '{RecordingPath}'.", recordingFile.RelativePath);
            }
        }

        if (selectionChanged)
            await selectionRepository.SaveAsync(packageRoot, trainingSelection, cancellationToken);

        logger.LogInformation("Imported {RecordingCount} REC recording(s) into the JDI package.", importedCount);
    }

    private static bool ImportExclusions(
        IntermediateSongPackage package,
        MotionRecordingDocument recording,
        IReadOnlyList<UbiArtSnipSelection> selections,
        MotionTrainingSelectionDocument trainingSelection)
    {
        MoveTimeline? timeline = package.CoachTimelines.FirstOrDefault(item => item.CoachId == recording.CoachId);
        if (timeline == null)
            return false;

        bool changed = false;
        Dictionary<string, int> occurrences = new(StringComparer.OrdinalIgnoreCase);
        foreach (MoveClip clip in timeline.Clips.OrderBy(static clip => clip.StartTime))
        {
            int occurrence = occurrences.GetValueOrDefault(clip.MoveId);
            occurrences[clip.MoveId] = occurrence + 1;
            bool isExcluded = selections.Any(selection =>
                !selection.IsIncluded &&
                string.Equals(selection.MoveId, clip.MoveId, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(selection.StartBeat - (clip.StartTime / 24.0)) <= (1.0 / 48.0));
            if (!isExcluded)
                continue;

            trainingSelection.SetExcluded(
                recording.RecordingId,
                recording.CoachId,
                clip.Id,
                clip.MoveId,
                occurrence,
                isExcluded: true);
            changed = true;
        }

        return changed;
    }

    private static IReadOnlyList<UbiArtSnippingRow> ReadSnippingRows(
        JustDanceUbiArtFileSystem fileSystem,
        string timelineFolder,
        ILogger logger)
    {
        List<UbiArtSnippingRow> result = [];
        foreach (CookedFile file in FindFiles(fileSystem, timelineFolder, "Snipping", "*_validation.csv"))
        {
            try
            {
                using Stream stream = fileSystem.GetFileStream(file);
                result.AddRange(UbiArtSnippingTable.Read(stream, Path.GetFileName(file.RelativePath)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
            {
                logger.LogWarning(ex, "Failed to import recording snipping table '{SnippingPath}'.", file.RelativePath);
            }
        }
        return result;
    }

    private static CookedFile[] FindFiles(
        JustDanceUbiArtFileSystem fileSystem,
        string timelineFolder,
        string folderName,
        string pattern)
    {
        string[] candidates =
        [
            Path.Combine(timelineFolder, folderName),
            Path.Combine(timelineFolder, folderName.ToLowerInvariant()),
            Path.Combine(timelineFolder, folderName + "s"),
            Path.Combine(timelineFolder, (folderName + "s").ToLowerInvariant())
        ];
        return
        [
            .. candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(candidate => fileSystem.GetAllFiles(candidate, pattern))
                .GroupBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
        ];
    }

    private static UbiArtSnippingRow? FindSnippingRow(IReadOnlyList<UbiArtSnippingRow> rows, string recordingFileName)
    {
        UbiArtSnippingRow? exact = rows.FirstOrDefault(row =>
            string.Equals(row.RecordingFileName, recordingFileName, StringComparison.OrdinalIgnoreCase));
        return exact ?? rows.FirstOrDefault(row =>
            recordingFileName.Contains(Path.GetFileNameWithoutExtension(row.RecordingFileName), StringComparison.OrdinalIgnoreCase));
    }
}