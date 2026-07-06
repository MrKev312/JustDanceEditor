using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingLibraryService(IMotionRecordingRepository repository)
{
    public async Task<IReadOnlyList<RecordingListItem>> LoadRecordingsAsync(TimelineEditorViewModel timeline)
    {
        List<RecordingListItem> result = [];
        IReadOnlyList<string> paths = repository.ListRecordingFiles(timeline.RootPath);
        foreach (string path in paths.OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                MotionRecordingDocument recording = await repository.LoadAsync(path);
                result.Add(new RecordingListItem(path, recording));
            }
            catch
            {
                // Ignore malformed attempts in the selector; deleting the file manually remains possible.
            }
        }

        return result;
    }

    public async Task<MotionRecordingDocument[]> LoadCoachRecordingsAsync(TimelineEditorViewModel timeline, int coachId)
    {
        string[] paths = [.. repository.ListRecordingFiles(timeline.RootPath, coachId)];
        Task<MotionRecordingDocument>[] loads = [.. paths.Select(path => repository.LoadAsync(path))];

        return await Task.WhenAll(loads);
    }

    public async Task<List<RecordingSelectionItem>> LoadCoachRecordingSelectionItemsAsync(TimelineEditorViewModel timeline, int coachId)
    {
        string[] paths = [.. repository
            .ListRecordingFiles(timeline.RootPath, coachId)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)];

        List<RecordingSelectionItem> result = [];
        foreach (string path in paths)
        {
            MotionRecordingDocument recording = await repository.LoadAsync(path);
            result.Add(new RecordingSelectionItem(path, Path.GetFileName(path), recording));
        }

        return result;
    }
}