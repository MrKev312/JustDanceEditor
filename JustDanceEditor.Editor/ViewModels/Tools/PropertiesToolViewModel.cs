using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;
using Avalonia.Media;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Properties", "View/Tools")]
public partial class PropertiesToolViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    private ObservableCollection<PropertyCategoryViewModel> _categories = [];

    [ObservableProperty]
    private object? _selectedObject;

    public PropertiesToolViewModel()
    {
         TimelineContext?.PropertyChanged += Context_PropertyChanged;
    }

    protected override void HandleActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        base.HandleActiveTimelineChanged(value);
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
                        var propVm = new PropertyItemViewModel(selection, item.Property.Name, item.Attribute!, ActiveTimeline);
                        
                        // Special handling for Color property on Clips: only MoveClip (Coach) allows color editing.
                        if (item.Property.Name is "BackgroundColor" or "Color")
                        {
                            if (first is ClipViewModel cv)
                            {
                                if (cv.RawClip is not MoveClip)
                                    continue; // Skip color for non-moves
                            }
                        }

                        // Populate Options if "Name"
                        if (item.Property.Name == "Name")
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
        if (firstClip.RawClip is MoveClip)
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
        else if (firstClip.RawClip is PictogramClip)
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
            catch {}
        }
    }
    public override string ToString() => Name;
}

public class PropertyCategoryViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<PropertyItemViewModel> Properties { get; } = [];
}

public partial class PropertyItemViewModel : ObservableObject
{
    private readonly List<object> _targets;
    private readonly PropertyInfo? _propertyInfoTemplate; 
    private readonly string _propertyName;
    private readonly TimelineEditorViewModel _timeline;

    public string Name { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTextBox))]
    public partial System.Collections.IEnumerable? Options { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; set; } = true;
    
    public PropertyItemViewModel(List<object> targets, string propertyName, InspectableAttribute attribute, TimelineEditorViewModel timeline)
    {
        _targets = targets;
        _propertyName = propertyName;
        _propertyInfoTemplate = targets[0].GetType().GetProperty(propertyName);
        _timeline = timeline;
        Name = attribute.DisplayName;
        IsReadOnly = attribute.IsReadOnly; 
        
        // Listen to external changes on the target objects
        foreach (var target in _targets)
        {
            if (target is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == _propertyName)
                    {
                        OnPropertyChanged(nameof(Value));
                    }
                };
            }
        }
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
            
            _timeline.PushUndo(
                undo: () => 
                { 
                    for(int i=0; i<_targets.Count; i++)
                        SetValue(_targets[i], oldValues[i]);
                },
                redo: () => 
                { 
                    for(int i=0; i<_targets.Count; i++)
                        SetValue(_targets[i], value);
                }
            );

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
        get => Value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";
        set
        {
             // Try parsing. If it lacks alpha, Color.Parse usually assumes FF or 00 depending on method.
             // We want to force Alpha to FF.
             if (Color.TryParse(value, out Color c))
             {
                 ColorValue = new Color(255, c.R, c.G, c.B);
             }
        }
    }

    private bool IsBinding = false;

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
