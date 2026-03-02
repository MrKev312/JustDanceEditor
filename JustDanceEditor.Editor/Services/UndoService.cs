using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Default implementation of IUndoService.
/// Manages independent undo/redo stacks for a timeline.
/// </summary>
public class UndoService : IUndoService
{
    // Each recorded action receives a unique monotonically-increasing sequence
    // number. Tracking the sequence at the top of the undo stack (0 = empty)
    // lets us detect dirty state correctly even when undo + new-record reaches
    // the same stack *depth* as the saved position but at a different history branch.
    private long _nextSequence = 1;
    private long _topSequence = 0;      // current top-of-undo-stack sequence (0 = empty)
    private long _savepointSequence = 0; // sequence when last saved (0 = initial clean)

    private readonly Stack<(Action Undo, Action Redo, long Seq)> _undoStack = new();
    private readonly Stack<(Action Undo, Action Redo, long Seq)> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public bool IsDirty => _topSequence != _savepointSequence;

    public void MarkSaved() => _savepointSequence = _topSequence;

    public event EventHandler? StateChanged;

    public void Record(Action undo, Action redo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);

        long seq = _nextSequence++;
        _undoStack.Push((undo, redo, seq));
        _redoStack.Clear();
        _topSequence = seq;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        (Action undo, Action redo, long seq) = _undoStack.Pop();
        undo();
        _redoStack.Push((undo, redo, seq));
        _topSequence = _undoStack.TryPeek(out (Action Undo, Action Redo, long Seq) top) ? top.Seq : 0;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        (Action undo, Action redo, long seq) = _redoStack.Pop();
        redo();
        _undoStack.Push((undo, redo, seq));
        _topSequence = seq;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
            return;

        _undoStack.Clear();
        _redoStack.Clear();
        _topSequence = 0;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}