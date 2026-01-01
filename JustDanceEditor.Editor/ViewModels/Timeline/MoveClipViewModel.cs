using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class MoveClipViewModel : ClipViewModel
{
    [Inspectable("Move Id", "Move")]
    [ObservableProperty]
    public partial string MoveId { get; set; } = string.Empty;

    public bool IsFullBody { get; }

    public MoveClipViewModel(MoveClip clip, double duration, Color color, string moveId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null, bool isFullBody = false)
        : base(clip, duration, color, moveId, rootPath, parentTimeline)
    {
        MoveId = clip.MoveId;
        Name = moveId;
        IsFullBody = isFullBody;

        // When duration changes, broadcast so UI can update
        this.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats))
            {
                NotifyClipDataChanged(nameof(DurationBeats));

                // Also synchronize duration across sibling moves of the same body type and MoveId
                if (_parentTimeline != null)
                {
                    var siblings = _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().Where(c => c.MoveId == this.MoveId && c.IsFullBody == this.IsFullBody).ToList();
                    foreach (var sibling in siblings)
                    {
                        if (!ReferenceEquals(sibling, this) && System.Math.Abs(sibling.DurationBeats - DurationBeats) > 1e-9)
                            sibling.DurationBeats = DurationBeats;
                    }
                }
            }
        };
    }

    partial void OnMoveIdChanged(string value)
    {
        if (RawClip is MoveClip m)
        {
            m.MoveId = value;
            Name = value;

            // If other clips with this MoveId exist within the same body type, adopt their color to stay in sync
            if (_parentTimeline != null)
            {
                var sibling = _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().FirstOrDefault(c => c != this && c.MoveId == value && c.IsFullBody == this.IsFullBody);
                if (sibling != null)
                {
                    // adopt sibling color
                    if (!Equals(this.BackgroundColor, sibling.BackgroundColor))
                        this.BackgroundColor = sibling.BackgroundColor;
                }
                else
                {
                    // Try to lookup default move definition color from timeline package (respecting body type)
                    if (_parentTimeline.TryGetCoachMoveColor(value, out var defColor))
                    {
                        if (!Equals(this.BackgroundColor, defColor))
                            this.BackgroundColor = defColor;
                    }
                }
            }

            NotifyClipDataChanged(nameof(MoveId));
        }
    }

    protected override void OnBackgroundColorChangedCore(Color value)
    {
        // When a move's color changes, synchronize all other moves with the same MoveId and same body type
        if (_parentTimeline == null)
        {
            base.OnBackgroundColorChangedCore(value);
            return;
        }

        var siblings = _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().Where(c => c.MoveId == this.MoveId && c.IsFullBody == this.IsFullBody).ToList();
        foreach (var s in siblings)
        {
            if (!ReferenceEquals(s, this) && !Equals(s.BackgroundColor, value))
            {
                s.BackgroundColor = value;
            }
        }

        // Also notify listeners
        base.OnBackgroundColorChangedCore(value);
    }
    private bool parentHasMoveDefinition(string id)
    {
        if (_parentTimeline == null) return false;
        return _parentTimeline.AvailableHandCoachMoves.Contains(id) || _parentTimeline.AvailableFullBodyCoachMoves.Contains(id);
    }
}