using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Properties", "View/Tools")]
public partial class PropertiesToolViewModel : TimelineToolViewModel, IDisposable
{
    private readonly InspectablePropertyRegistry _properties;

    [ObservableProperty]
    public partial ObservableCollection<PropertyCategoryViewModel> Categories { get; set; } = [];

    [ObservableProperty]
    public partial object? SelectedObject { get; set; }

    public PropertiesToolViewModel(
        ITimelineContextService? timelineContext = null,
        InspectablePropertyRegistry? properties = null)
        : base(timelineContext)
    {
        _properties = properties ?? new InspectablePropertyRegistry();
        TimelineContext?.PropertyChanged += Context_PropertyChanged;
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline) => RefreshProperties();
    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline) => RefreshProperties();

    public override void Dispose()
    {
        TimelineContext?.PropertyChanged -= Context_PropertyChanged;
        ClearProperties();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.SelectedObjects))
            RefreshProperties();
    }

    private void RefreshProperties()
    {
        ClearProperties();
        List<object>? selection = TimelineContext?.SelectedObjects;
        if (selection is not { Count: > 0 } || ActiveTimeline == null)
            return;

        TimelineEditorViewModel timeline = ActiveTimeline;
        SelectedObject = selection.Count == 1 ? selection[0] : null;
        IReadOnlyList<ResolvedInspectableProperty> resolved = _properties.Resolve(selection, timeline);
        foreach (IGrouping<string, ResolvedInspectableProperty> group in resolved.GroupBy(static item => item.Descriptor.Category))
        {
            PropertyCategoryViewModel category = new(group.Key);
            foreach (ResolvedInspectableProperty item in group)
            {
                PropertyItemViewModel property = new(
                    [.. item.Targets],
                    item.Descriptor,
                    timeline.UndoService)
                {
                    Options = item.Options,
                    IsEditable = item.IsEditable
                };
                category.Properties.Add(property);
            }

            if (category.Properties.Count > 0)
                Categories.Add(category);
        }
    }

    private void ClearProperties()
    {
        foreach (PropertyItemViewModel property in Categories.SelectMany(static category => category.Properties))
            property.Dispose();
        Categories.Clear();
        SelectedObject = null;
    }
}
