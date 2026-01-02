using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Default implementation of IUndoService.
/// Manages independent undo/redo stacks for a timeline.
/// </summary>
public class UndoService : IUndoService
{
    private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
    private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? StateChanged;

    public void Record(Action undo, Action redo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(redo);

        _undoStack.Push((undo, redo));
        _redoStack.Clear();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        (Action? undo, Action? redo) = _undoStack.Pop();
        undo();
        _redoStack.Push((undo, redo));

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        (Action? undo, Action? redo) = _redoStack.Pop();
        redo();
        _undoStack.Push((undo, redo));

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
            return;

        _undoStack.Clear();
        _redoStack.Clear();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}