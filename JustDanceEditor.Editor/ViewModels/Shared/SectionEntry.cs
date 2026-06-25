using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

/// <summary>
/// Represents a single section entry used in song creation and editing dialogs.
/// </summary>
public partial class SectionEntry : ObservableObject
{
    [ObservableProperty]
    public partial SongSectionType SectionType { get; set; }

    [ObservableProperty]
    public partial double StartBeat { get; set; }
}