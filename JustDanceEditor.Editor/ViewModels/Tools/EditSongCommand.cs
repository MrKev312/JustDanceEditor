using Avalonia.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Views.Dialogs;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Edit Song Properties...", "File", priority: 100)]
public class EditSongCommand(IWindowService windows, IEditorPromptService prompts) : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        _ = RunAsync(timelineContext);
    }

    private async Task RunAsync(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        try
        {
            Window? mainWindow = windows.MainWindow;
            if (mainWindow == null)
                return;

            // Block if there are unsaved changes (dirty relative to last save)
            if (timeline.UndoService.IsDirty)
            {
                await prompts.ShowMessageAsync(
                    "Unsaved Changes",
                    "You have unsaved changes. Please save your work before editing song properties.");
                return;
            }

            // Show the edit song dialog, pre-populated from the current timeline
            EditSongViewModel vm = new(timeline);
            EditSongWindow dialog = new() { DataContext = vm };

            await dialog.ShowDialog(mainWindow);

            EditSongResult? result = vm.Result;
            if (result == null)
                return;

            // Apply changes to the package and save to disk
            ApplyEdits(timeline, result);

            // Refresh the timeline view in-place (no close/reopen needed)
            await timeline.RebuildFromPackageAsync();
        }
        catch (Exception ex)
        {
            await prompts.ShowErrorAsync(
                "Error Editing Song",
                $"Failed to apply song edits:\n{ex.Message}",
                ex);
        }
    }

    private static void ApplyEdits(TimelineEditorViewModel timeline, EditSongResult result)
    {
        IntermediateSongPackage package = timeline.Package;

        // 1. Update metadata
        package.Metadata.MapName = result.MapName;
        package.Metadata.Title = result.Title;
        package.Metadata.Artist = result.Artist;
        package.Metadata.CoachCount = result.CoachCount;
        package.Metadata.Difficulty = result.Difficulty;

        // 2–4. Rebuild markers, sections, signatures via shared builder
        double beatDuration = 60.0 / result.Bpm;
        int startBeat = result.StartBeat;
        int endBeat = result.EndBeat;
        int markerCount = endBeat - startBeat + 1;

        List<int> markers = SongStructureBuilder.BuildMarkers(result.Bpm, startBeat, endBeat);
        List<SectionSegment> sections = SongStructureBuilder.BuildSections(result.Sections);
        List<SignatureSegment> signatures = SongStructureBuilder.BuildDefaultSignatures(result.BeatsPerMeasure);

        // 5. Update the structure document in-place
        TimelineStructureDocument ts = package.TimelineStructure;
        ts.Markers = markers;
        ts.Sections = sections;
        ts.Signatures = signatures;
        ts.StartBeat = startBeat;
        ts.EndBeat = endBeat;

        // Update map length
        package.Metadata.MapLengthSeconds = markerCount * beatDuration;

        // Ensure coach timelines exist for new coach count
        while (package.CoachTimelines.Count < result.CoachCount)
        {
            package.CoachTimelines.Add(new MoveTimeline
            {
                CoachId = package.CoachTimelines.Count,
                TrackId = package.CoachTimelines.Count + 1
            });
        }

        // 6. Save to disk (RebuildFromPackageAsync will sync the viewmodel properties)
        IntermediatePackageSerializer.WriteToFolder(package, timeline.RootPath);
    }
}
