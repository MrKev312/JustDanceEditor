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

public class PictogramClipViewModel : ClipViewModel, IHasDynamicOptions
{
    private PictogramClip PictogramClip => (PictogramClip)RawClip;

    [Inspectable("Pictogram Id", "Pictogram")]
    public string PictogramId
    {
        get => PictogramClip.PictogramId ?? string.Empty;
        set
        {
            value ??= string.Empty;
            if (string.Equals(PictogramClip.PictogramId, value, StringComparison.Ordinal))
                return;

            PictogramClip.PictogramId = value;
            UpdateImagePath(value);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(PictogramId)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
            NotifyClipDataChanged(nameof(PictogramId));
            NotifyClipDataChanged(nameof(Name));
        }
    }

    public override bool IsResizable => true;

    public override string Name
    {
        get => PictogramId;
        set => PictogramId = value;
    }

    public PictogramClipViewModel(PictogramClip clip, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, Colors.LightBlue, clip.PictogramId ?? string.Empty, rootPath, parentTimeline)
    {
        UpdateImagePath(PictogramId);
    }

    protected override int GetDurationFrames() => PictogramClip.Duration;

    protected override void SetDurationFrames(int frames) => PictogramClip.Duration = frames;

    private void UpdateImagePath(string pictogramId)
    {
        ImagePath = !string.IsNullOrEmpty(pictogramId) && _rootPath != null
            ? Path.Combine(_rootPath, "assets", "pictograms", $"{pictogramId}.webp")
            : null;
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