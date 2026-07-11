using Avalonia.Controls;
using Avalonia.Platform.Storage;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Lyrics;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Import Lyrics", "Timeline")]
public class ImportLyricsCommand(IWindowService windows) : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        _ = ImportLyricsAsync(timeline);
    }

    private async Task ImportLyricsAsync(TimelineEditorViewModel timeline)
    {
        Window? topLevel = windows.MainWindow;
        if (topLevel == null)
            return;

        LyricImporterRegistry registry = LyricImporterRegistry.Instance;
        List<string> patterns = [.. registry.AllSupportedExtensions.Select(e => $"*.{e}")];

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Lyric File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Lyric Files") { Patterns = patterns },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count == 0)
            return;

        IStorageFile file = files[0];
        string extension = Path.GetExtension(file.Name);
        string content;
        await using (Stream stream = await file.OpenReadAsync())
        using (StreamReader reader = new(stream))
            content = await reader.ReadToEndAsync();

        ILyricImporter? importer = registry.Resolve(extension, content);
        if (importer == null)
            return;

        IReadOnlyList<LyricLine> lines = importer.Parse(content);
        if (lines.Count == 0)
            return;

        TrackViewModel? lyricsTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Lyrics);
        if (lyricsTrack == null)
            return;

        TimelineStructureDocument ts = timeline.Package.TimelineStructure;
        Avalonia.Media.Color lyricsColor = timeline.LyricsDefinitionColor;

        const float Pitch = 8.175798f;
        const int ContentType = 2;
        KaraokeTolerance tolerances = new() { StartTimeTolerance = 4, EndTimeTolerance = 4, SemitoneTolerance = 5 };

        List<KaraokeClipViewModel> newClips = [];
        foreach (LyricLine line in lines)
        {
            double beatLabel = ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(line.StartSeconds));
            int startTicks = (int)Math.Round(beatLabel * 24.0);

            int durationTicks;
            if (line.EndSeconds > line.StartSeconds)
            {
                double endBeatLabel = ts.GetBeatLabelFromIndex(ts.GetBeatAtSeconds(line.EndSeconds));
                durationTicks = Math.Max(1, (int)Math.Round((endBeatLabel - beatLabel) * 24.0));
            }
            else
            {
                durationTicks = 24; // default: 1 beat
            }

            KaraokeClip raw = new()
            {
                StartTime = startTicks,
                Duration = durationTicks,
                Lyrics = line.Text,
                IsEndOfLine = line.IsEndOfLine,
                Pitch = Pitch,
                ContentType = ContentType,
                Tolerances = tolerances,
            };

            KaraokeClipViewModel clipVm = new(raw, timeline.RootPath, timeline);
            newClips.Add(clipVm);
        }

        if (newClips.Count == 0)
            return;

        List<KaraokeClipViewModel> snapshot = [.. newClips];
        timeline.PushUndo(
            undo: () =>
            {
                foreach (KaraokeClipViewModel c in snapshot)
                    lyricsTrack.Clips.Remove(c);
            },
            redo: () =>
            {
                foreach (KaraokeClipViewModel c in snapshot)
                    if (!lyricsTrack.Clips.Contains(c))
                        lyricsTrack.Clips.Add(c);
            });

        foreach (KaraokeClipViewModel c in newClips)
            lyricsTrack.Clips.Add(c);
    }
}
