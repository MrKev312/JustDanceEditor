using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Messaging;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Properties", "View/Tools")]
public partial class PropertiesToolViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<PropertyCategoryViewModel> Categories { get; set; } = [];

    [ObservableProperty]
    public partial object? SelectedObject { get; set; }

    public PropertiesToolViewModel()
    {
        TimelineContext?.PropertyChanged += Context_PropertyChanged;
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        RefreshProperties();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        // Dispose and clear properties when the timeline is detached
        RefreshProperties();
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.SelectedObjects))
        {
            RefreshProperties();
        }
    }

    private void RefreshProperties()
    {
        // Dispose existing PropertyItemViewModels to unregister messenger handlers and avoid leaks
        foreach (var cat in Categories.ToList())
        {
            foreach (var prop in cat.Properties.ToList())
            {
                if (prop is IDisposable d)
                    d.Dispose();
            }
        }

        Categories.Clear();
        SelectedObject = null;

        List<object>? selection = TimelineContext?.SelectedObjects;

        if (selection != null && selection.Count > 0 && ActiveTimeline != null)
        {
            SelectedObject = selection.Count == 1 ? selection[0] : null;

            // 1. Identify common properties
            var first = selection[0];
            var templateProps = first.GetType().GetProperties()
                .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<InspectableAttribute>() })
                .Where(x => x.Attribute != null)
                .OrderBy(x => x.Attribute!.Category);

            var groups = templateProps.GroupBy(x => x.Attribute!.Category);

            foreach (var group in groups)
            {
                var categoryVm = new PropertyCategoryViewModel(group.Key);
                foreach (var item in group)
                {
                    // Verify this property exists and has the same attribute on all selected objects
                    bool consistent = selection.All(o =>
                    {
                        PropertyInfo? p = o.GetType().GetProperty(item.Property.Name);
                        return p != null && p.GetCustomAttribute<InspectableAttribute>() != null;
                    });

                    if (consistent)
                    {
                        var propVm = new PropertyItemViewModel(
                            selection,
                            item.Property.Name,
                            item.Attribute!,
                            ActiveTimeline!.UndoService,
                            ActiveTimeline.TimelineStructure,
                            ActiveTimeline.Tracks,
                            ActiveTimeline);

                        // Special handling for Color property on Clips: allow MoveClip (Coach) and KaraokeClip (Lyrics)
                        if (item.Property.Name is "BackgroundColor" or "Color")
                        {
                            if (first is ClipViewModel cv)
                            {
                                // With strongly-typed ClipViewModels we can check concrete types directly
                                if (cv is not (MoveClipViewModel or KaraokeClipViewModel))
                                    continue; // Skip color for non-moves and non-lyrics
                            }
                        }

                        // Populate Options for MoveId and PictogramId
                        if (item.Property.Name == "MoveId" || item.Property.Name == "PictogramId")
                        {
                            PopulateOptions(propVm, selection, ActiveTimeline);
                        }

                        categoryVm.Properties.Add(propVm);
                    }
                }

                if (categoryVm.Properties.Count > 0)
                    Categories.Add(categoryVm);
            }
        }
    }

    private void PopulateOptions(PropertyItemViewModel propVm, List<object> selection, TimelineEditorViewModel timeline)
    {
        // Check if we are dealing with ClipViewModels
        var firstClip = selection[0] as ClipViewModel;
        if (firstClip == null)
            return;

        // Determine type of clip
        if (firstClip is MoveClipViewModel)
        {
            // Find track for firstClip
            TrackViewModel? track = timeline.Tracks.FirstOrDefault(t => t.Clips.Contains(firstClip));
            if (track != null)
            {
                // Heuristic: Check Title
                if (track.Title.IndexOf("FullBody", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    propVm.Options = timeline.AvailableFullBodyCoachMoves.ToList();
                    propVm.IsEditable = false;
                }
                else
                {
                    propVm.Options = timeline.AvailableHandCoachMoves.ToList();
                    propVm.IsEditable = false;
                }
            }
        }
        else if (firstClip is PictogramClipViewModel)
        {
            var list = new List<PictogramOptionViewModel>();
            var dir = System.IO.Path.Combine(timeline.RootPath, "assets", "pictograms");
            if (System.IO.Directory.Exists(dir))
            {
                var files = System.IO.Directory.GetFiles(dir);
                foreach (var file in files)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    // If multiple extensions exist for same name, first one wins
                    if (!list.Any(x => x.Name == name))
                    {
                        list.Add(new PictogramOptionViewModel(name, file));
                    }
                }
                // Sort by name
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            propVm.Options = list;
            propVm.IsEditable = true; // Allow custom
        }
    }
}

public class PictogramOptionViewModel
{
    public string Name { get; }
    public string ImagePath { get; }
    public Avalonia.Media.Imaging.Bitmap? PreviewImage { get; }

    public PictogramOptionViewModel(string name, string path)
    {
        Name = name;
        ImagePath = path;
        if (System.IO.File.Exists(path))
        {
            try
            {
                PreviewImage = new Avalonia.Media.Imaging.Bitmap(path);
            }
            catch { }
        }
    }
    public override string ToString() => Name;
}

public class PropertyCategoryViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<PropertyItemViewModel> Properties { get; } = [];
}

public partial class PropertyItemViewModel : ObservableObject, IDisposable
{
    private readonly List<object> _targets;
    private readonly PropertyInfo? _propertyInfoTemplate;
    private readonly string _propertyName;
    private readonly IUndoService _undoService;
    private readonly TimelineStructureDocument _timelineStructure;
    private readonly ObservableCollection<TrackViewModel> _tracks;
    private readonly JustDanceEditor.Editor.Services.ILyricsColorService _lyricsService;
    private bool _isColorPickerActive = false;
    private object? _colorPickerInitialValue;

    private record ColorSnapshot(bool IsLyrics, List<(ClipViewModel Clip, Color Color)> ClipColors, string? OldMetadata, List<Color>? FinalColors = null);

    public string Name { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTextBox))]
    public partial System.Collections.IEnumerable? Options { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; set; } = true;

    public PropertyItemViewModel(
        List<object> targets,
        string propertyName,
        InspectableAttribute attribute,
        IUndoService undoService,
        TimelineStructureDocument timelineStructure,
        ObservableCollection<TrackViewModel> tracks,
        JustDanceEditor.Editor.Services.ILyricsColorService lyricsService)
    {
        _targets = targets;
        _propertyName = propertyName;
        _propertyInfoTemplate = targets[0].GetType().GetProperty(propertyName);
        _undoService = undoService;
        _timelineStructure = timelineStructure;
        _tracks = tracks;
        _lyricsService = lyricsService;
        Name = attribute.DisplayName;
        IsReadOnly = attribute.IsReadOnly;

        // Listen to external changes on the target objects via weak messaging to avoid leaking references
        // Register a single weak messenger handler to avoid multiple subscriptions when there are multiple targets
        if (_targets.Any(t => t is INotifyPropertyChanged))
        {
            // Listen for ClipDataChangedMessage on the default channel (no token)
            WeakReferenceMessenger.Default.Register<PropertyItemViewModel, JustDanceEditor.Editor.Messaging.ClipDataChangedMessage>(this, (r, m) =>
            {
                if (m.PropertyName == _propertyName && _targets.Any(t => ReferenceEquals(m.Source, t)))
                {
                    // Notify recipient to refresh bound Value
                    r.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
                }
            });

            // Ensure we unregister when this view model is disposed
            // (Unregister will be called from Dispose())
        }
    }

    /// <summary>
    /// Notifies that the color picker has opened.
    /// </summary>
    public void OnColorPickerOpened()
    {
        _isColorPickerActive = true;
        _colorPickerInitialValue = null;

        // If editing a ClipViewModel's BackgroundColor, capture a full snapshot
        if (_targets.Count > 0 && _targets[0] is ClipViewModel cv)
        {
            // Lyrics: capture all lyrics clips and metadata
            if (cv is KaraokeClipViewModel)
            {
                var allLyrics = _tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().ToList();
                var colors = allLyrics.Select(c => ((ClipViewModel)c, c.BackgroundColor)).ToList();
                var snapshot = new ColorSnapshot(true, colors, _lyricsService.GetLyricsColor(), null);
                _colorPickerInitialValue = snapshot;
            }
            else if (cv is MoveClipViewModel)
            {
                // For moves, capture only the selected targets
                var selected = _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor)).ToList();
                var snapshot = new ColorSnapshot(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
            else
            {
                var selected = _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor)).ToList();
                var snapshot = new ColorSnapshot(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
        }
        else
        {
            // Fallback: store the simple initial value
            _colorPickerInitialValue = GetValue(_targets[0]);
        }
    }

    /// <summary>
    /// Notifies that the color picker has closed. This pushes the change to the undo stack.
    /// </summary>
    public void OnColorPickerClosed()
    {
        _isColorPickerActive = false;

        // If we have a ColorSnapshot, push a single composite undo/redo
        if (_colorPickerInitialValue is ColorSnapshot snap)
        {
            if (snap.IsLyrics)
            {
                // Determine final color (assume all lyrics were set to same color by live updates)
                var firstLyrics = _tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().FirstOrDefault();
                if (firstLyrics == null)
                {
                    _colorPickerInitialValue = null;
                    return;
                }

                var finalColor = firstLyrics.BackgroundColor;
                var initialColors = snap.ClipColors.Select(cc => cc.Color).ToList();
                var affectedClips = snap.ClipColors.Select(cc => cc.Clip).ToList();
                var oldMetadata = snap.OldMetadata;
                var newMetadata = ClipViewModel.ColorToRgbaHex(finalColor);

                // Only push undo if the color actually changed
                bool colorChanged = !Equals(initialColors[0], finalColor) || oldMetadata != newMetadata;
                if (colorChanged)
                {
                    // Push one undo that restores all original colors and metadata
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < affectedClips.Count; i++)
                            {
                                affectedClips[i].BackgroundColor = initialColors[i];
                            }
                            _lyricsService.UpdateLyricsColor(oldMetadata ?? "#FFFFFFFF");
                        },
                        redo: () =>
                        {
                            // Set current lyrics clips to the final color (handles changed set of clips)
                            var lyricsClips = _tracks.SelectMany(t => t.Clips).OfType<KaraokeClipViewModel>().ToList();
                            foreach (var clip in lyricsClips)
                            {
                                clip.BackgroundColor = finalColor;
                            }
                            _lyricsService.UpdateLyricsColor(newMetadata);
                        }
                    );
                }
            }
            else
            {
                // Non-lyrics: use the captured list to create undo/redo per captured clip
                var initialList = snap.ClipColors.Select(cc => cc.Color).ToList();
                var clips = snap.ClipColors.Select(cc => cc.Clip).ToList();
                var finalList = clips.Select(c => c.BackgroundColor).ToList();

                bool anyChanged = false;
                for (int i = 0; i < clips.Count; i++)
                {
                    if (!Equals(initialList[i], finalList[i]))
                    {
                        anyChanged = true;
                        break;
                    }
                }

                if (anyChanged)
                {
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = initialList[i];
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = finalList[i];
                        }
                    );
                }
            }

            _colorPickerInitialValue = null;
            return;
        }

        // Fallback behavior when we didn't capture snapshot: push if value changed
        var finalValue = GetValue(_targets[0]);
        if (!Equals(_colorPickerInitialValue, finalValue))
        {
            var oldValues = _targets.Select(t => _colorPickerInitialValue).ToList();
            _undoService.Record(
                undo: () =>
                {
                    for (int i = 0; i < _targets.Count; i++)
                        SetValue(_targets[i], oldValues[i]);
                },
                redo: () =>
                {
                    for (int i = 0; i < _targets.Count; i++)
                        SetValue(_targets[i], finalValue);
                }
            );
            // Ensure we unregister when this view model is disposed (see Dispose() below)
        }

        _colorPickerInitialValue = null;
    }

    public object? Value
    {
        get
        {
            // Return value if all are same, else null
            var firstVal = GetValue(_targets[0]);
            for (int i = 1; i < _targets.Count; i++)
            {
                if (!Equals(GetValue(_targets[i]), firstVal))
                    return null;
            }

            return firstVal;
        }
        set
        {
            if (value == null)
                return; // Don't set nulls explicitly (e.g. from empty selection)

            var oldValues = _targets.Select(GetValue).ToList();

            // Only push undo if not in color picker mode
            if (!_isColorPickerActive)
            {
                _undoService.Record(
                    undo: () =>
                    {
                        for (int i = 0; i < _targets.Count; i++)
                            SetValue(_targets[i], oldValues[i]);
                    },
                    redo: () =>
                    {
                        for (int i = 0; i < _targets.Count; i++)
                            SetValue(_targets[i], value);
                    }
                );
            }

            foreach (var target in _targets)
            {
                SetValue(target, value);
            }

            OnPropertyChanged(nameof(Value));
        }
    }

    private object? GetValue(object target) => target.GetType().GetProperty(_propertyName)?.GetValue(target);
    private void SetValue(object target, object? val) => target.GetType().GetProperty(_propertyName)?.SetValue(target, val);

    public Type PropertyType => _propertyInfoTemplate!.PropertyType;

    // Binding Helpers
    public bool IsColor => PropertyType == typeof(Color);
    public bool IsBool => PropertyType == typeof(bool);
    public bool IsStringOrNumber => PropertyType == typeof(string) || IsNumber;
    public bool IsNumber => PropertyType == typeof(double) || PropertyType == typeof(int) || PropertyType == typeof(float);
    public bool HasOptions => Options != null;
    public bool ShowTextBox => IsStringOrNumber && !HasOptions;

    public string StringValue
    {
        get => Value?.ToString() ?? "";
        set
        {
            if (IsBinding)
                return;
            try
            {
                IsBinding = true;
                if (PropertyType == typeof(string))
                    Value = value;
                else if (PropertyType == typeof(double) && double.TryParse(value, out double d))
                    Value = d;
                else if (PropertyType == typeof(int) && int.TryParse(value, out int i))
                    Value = i;
                else if (PropertyType == typeof(float) && float.TryParse(value, out float f))
                    Value = f;
            }
            finally
            {
                IsBinding = false;
            }
        }
    }

    public bool BoolValue
    {
        get => Value is bool b ? b : false;
        set => Value = value;
    }

    public Color ColorValue
    {
        get => Value is Color c ? c : Colors.Transparent;
        set => Value = value;
    }

    public object? SelectedOption
    {
        get
        {
            if (Options == null)
                return null;
            var currentVal = StringValue;
            foreach (var opt in Options)
            {
                if (opt is PictogramOptionViewModel p && p.Name == currentVal)
                    return opt;
                if (opt is string s && s == currentVal)
                    return opt;
            }

            return null;
        }
        set
        {
            if (value is PictogramOptionViewModel p)
                StringValue = p.Name;
            else if (value != null)
                StringValue = value.ToString() ?? "";
        }
    }

    public string HexString
    {
        get => Value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}" : "";
        set
        {
            // Try parsing as RGBA hex first (our format), then fall back to standard parsing
            if (!string.IsNullOrEmpty(value))
            {
                // Check if the target is a KaraokeClip (for which we use RGBA format)
                bool isLyricsClip = _targets.Count > 0 && _targets[0] is KaraokeClipViewModel;

                if (isLyricsClip)
                {
                    // Use RGBA parsing for lyrics
                    ColorValue = ClipViewModel.ParseRgbaHex(value);
                }
                else
                {
                    // For other clips, use standard ARGB parsing with full alpha
                    if (Color.TryParse(value, out Color c))
                    {
                        ColorValue = new Color(255, c.R, c.G, c.B);
                    }
                }
            }
        }
    }

    private bool IsBinding = false;

    public void Dispose()
    {
        // Unregister messenger handler for clip data changes
        try
        {
            WeakReferenceMessenger.Default.Unregister<JustDanceEditor.Editor.Messaging.ClipDataChangedMessage>(this);
        }
        catch { }
    }

    // Refresh typed properties when Value changes
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Value))
        {
            OnPropertyChanged(nameof(StringValue));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(ColorValue));
            OnPropertyChanged(nameof(HexString));
            OnPropertyChanged(nameof(SelectedOption));
        }
    }
}