using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class PictogramClipViewModel : ClipViewModel, IHasDynamicOptions
{
    [Inspectable("Pictogram Id", "Pictogram")]
    [ObservableProperty]
    public partial string PictogramId { get; set; } = string.Empty;

    public override bool IsResizable => true;

    public PictogramClipViewModel(PictogramClip clip, double duration, Color color, string pictogramId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, pictogramId, rootPath, parentTimeline)
    {
        PictogramId = clip.PictogramId ?? string.Empty;
        if (!string.IsNullOrEmpty(PictogramId) && rootPath != null)
            ImagePath = Path.Combine(rootPath, "assets", "pictograms", $"{PictogramId}.webp");
    }

    partial void OnPictogramIdChanged(string value)
    {
        if (RawClip is PictogramClip p)
        {
            p.PictogramId = value;
            if (!string.IsNullOrEmpty(value) && _rootPath != null)
                ImagePath = Path.Combine(_rootPath, "assets", "pictograms", $"{value}.webp");

            NotifyClipDataChanged(nameof(PictogramId));
        }
    }

    protected override void SyncRawDuration(int frames)
    {
        if (RawClip is PictogramClip p)
            p.Duration = frames;
    }

    // IHasDynamicOptions
    public IEnumerable<object>? GetDynamicOptions(string propertyName, TimelineEditorViewModel timeline)
    {
        if (propertyName != nameof(PictogramId))
            return null;

        List<PictogramOptionViewModel> list = [];
        string dir = Path.Combine(timeline.RootPath, "assets", "pictograms");
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.GetFiles(dir))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!seen.Contains(name))
                {
                    seen.Add(name);
                    list.Add(new PictogramOptionViewModel(name, file));
                }
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        // also include any pictogram IDs currently referenced by clips that are
        // missing from disk so users can still select/see them
        foreach (TrackViewModel track in timeline.Tracks)
        {
            foreach (PictogramClipViewModel clip in track.Clips.OfType<PictogramClipViewModel>())
            {
                string id = clip.PictogramId;
                if (!string.IsNullOrEmpty(id) && !seen.Contains(id))
                {
                    seen.Add(id);
                    string fakePath = Path.Combine(dir, id + ".webp");
                    list.Add(new PictogramOptionViewModel(id, fakePath));
                }
            }
        }

        return list;
    }

    public bool IsDynamicPropertyEditable(string propertyName) => true;
}