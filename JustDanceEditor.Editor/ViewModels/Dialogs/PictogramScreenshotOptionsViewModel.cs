using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public enum PictogramInsertionMode
{
    None,
    AllInstancesOfSelectedMove
}

public sealed class PictogramReferenceMoveOption
{
    public required string MoveId { get; init; }
    public required int StartFrame { get; init; }

    public double StartBeat => StartFrame / 24d;

    public string DisplayName => $"{MoveId} @ beat {StartBeat:0.###}";

    public override string ToString() => DisplayName;
}

public sealed class PictogramScreenshotOptionsResult
{
    public PictogramFrameLayoutMode FrameLayoutMode { get; set; } = PictogramFrameLayoutMode.TransparentBars;
    public PictogramHorizontalFocus HorizontalFocus { get; set; } = PictogramHorizontalFocus.Center;
    public PictogramInsertionMode InsertionMode { get; set; } = PictogramInsertionMode.None;
    public string? ReferenceMoveId { get; set; }
    public int? ReferenceMoveStartFrame { get; set; }
}

public partial class PictogramScreenshotOptionsViewModel : ObservableObject, IDialogResult<PictogramScreenshotOptionsResult>
{
    [ObservableProperty]
    public partial PictogramFrameLayoutMode SelectedFrameLayoutMode { get; set; } = PictogramFrameLayoutMode.TransparentBars;

    [ObservableProperty]
    public partial PictogramHorizontalFocus SelectedHorizontalFocus { get; set; } = PictogramHorizontalFocus.Center;

    [ObservableProperty]
    public partial bool ShowInsertionOptions { get; set; }

    [ObservableProperty]
    public partial PictogramInsertionMode SelectedInsertionMode { get; set; } = PictogramInsertionMode.None;

    [ObservableProperty]
    public partial PictogramReferenceMoveOption? SelectedReferenceMove { get; set; }

    public bool ShowHorizontalFocus => SelectedFrameLayoutMode == PictogramFrameLayoutMode.CropToFill;
    public bool ShowReferenceMoveOptions => ShowInsertionOptions && SelectedInsertionMode == PictogramInsertionMode.AllInstancesOfSelectedMove;

    public IReadOnlyList<PictogramFrameLayoutMode> FrameLayoutModes { get; } =
    [
        PictogramFrameLayoutMode.TransparentBars,
        PictogramFrameLayoutMode.CropToFill
    ];

    public IReadOnlyList<PictogramHorizontalFocus> HorizontalFocusOptions { get; } =
    [
        PictogramHorizontalFocus.Left,
        PictogramHorizontalFocus.Center,
        PictogramHorizontalFocus.Right
    ];

    public IReadOnlyList<PictogramInsertionMode> InsertionModes { get; } =
    [
        PictogramInsertionMode.None,
        PictogramInsertionMode.AllInstancesOfSelectedMove
    ];

    public IReadOnlyList<PictogramReferenceMoveOption> ReferenceMoveOptions { get; private set; } = [];

    public PictogramScreenshotOptionsResult? Result { get; private set; }

    public void EnableInsertionOptions(IEnumerable<PictogramReferenceMoveOption> referenceMoveOptions)
    {
        List<PictogramReferenceMoveOption> ordered = referenceMoveOptions
            .Where(o => !string.IsNullOrWhiteSpace(o.MoveId))
            .ToList();

        ReferenceMoveOptions = ordered;
        ShowInsertionOptions = true;
        OnPropertyChanged(nameof(ReferenceMoveOptions));
        OnPropertyChanged(nameof(ShowReferenceMoveOptions));

        if (ordered.Count == 0)
            SelectedInsertionMode = PictogramInsertionMode.None;
        else if (SelectedReferenceMove == null)
            SelectedReferenceMove = ordered[0];
    }

    partial void OnSelectedFrameLayoutModeChanged(PictogramFrameLayoutMode value)
    {
        OnPropertyChanged(nameof(ShowHorizontalFocus));
    }

    partial void OnShowInsertionOptionsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReferenceMoveOptions));
    }

    partial void OnSelectedInsertionModeChanged(PictogramInsertionMode value)
    {
        OnPropertyChanged(nameof(ShowReferenceMoveOptions));
    }

    public void Accept()
    {
        PictogramInsertionMode insertionMode = ShowInsertionOptions
            ? SelectedInsertionMode
            : PictogramInsertionMode.None;

        Result = new PictogramScreenshotOptionsResult
        {
            FrameLayoutMode = SelectedFrameLayoutMode,
            HorizontalFocus = SelectedHorizontalFocus,
            InsertionMode = insertionMode,
            ReferenceMoveId = insertionMode == PictogramInsertionMode.AllInstancesOfSelectedMove ? SelectedReferenceMove?.MoveId : null,
            ReferenceMoveStartFrame = insertionMode == PictogramInsertionMode.AllInstancesOfSelectedMove ? SelectedReferenceMove?.StartFrame : null
        };
    }

    public void Cancel() => Result = null;
}