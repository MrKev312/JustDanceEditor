using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

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
public class EditSongCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        _ = RunAsync(timelineContext);
    }

    private static async Task RunAsync(ITimelineContextService? timelineContext)
    {
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        try
        {
            IClassicDesktopStyleApplicationLifetime? desktop =
                Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            Window? mainWindow = desktop?.MainWindow;
            if (mainWindow == null)
                return;

            // Block if there are unsaved changes (dirty relative to last save)
            if (timeline.UndoService.IsDirty)
            {
                Window warnWin = new()
                {
                    Title = "Unsaved Changes",
                    Width = 420,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBlock
                    {
                        Text = "You have unsaved changes. Please save your work before editing song properties.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(16),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                };
                await warnWin.ShowDialog(mainWindow);
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
            IClassicDesktopStyleApplicationLifetime? desktop2 =
                Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            Window? mw = desktop2?.MainWindow;
            if (mw != null)
            {
                Window errorWin = new()
                {
                    Title = "Error Editing Song",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBlock
                    {
                        Text = $"Failed to apply song edits:\n{ex.Message}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(16)
                    }
                };
                await errorWin.ShowDialog(mw);
            }
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