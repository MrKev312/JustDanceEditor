using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Editor.Attributes;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class MoveClipViewModel : ClipViewModel
{
    [Inspectable("Move Id", "Move")]
    [ObservableProperty]
    public partial string MoveId { get; set; } = string.Empty;

    public MoveClipViewModel(MoveClip clip, double duration, Color color, string moveId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, moveId, rootPath, parentTimeline)
    {
        MoveId = clip.MoveId;
        Name = moveId;

        this.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats))
            {
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }

    partial void OnMoveIdChanged(string value)
    {
        if (RawClip is MoveClip m)
        {
            m.MoveId = value;
            Name = value;
            NotifyClipDataChanged(nameof(MoveId));
        }
    }

    private bool parentHasMoveDefinition(string id)
    {
        if (_parentTimeline == null) return false;
        return _parentTimeline.AvailableHandCoachMoves.Contains(id) || _parentTimeline.AvailableFullBodyCoachMoves.Contains(id);
    }
}
