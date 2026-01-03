using System;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Manages undo/redo operations for a timeline.
/// Each timeline has its own instance to maintain independent undo stacks.
/// </summary>
public interface IUndoService
{
    /// <summary>
    /// Gets whether undo is currently available.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Gets whether redo is currently available.
    /// </summary>
    bool CanRedo { get; }

    /// <summary>
    /// Records an action pair (undo and redo) on the undo stack.
    /// Clears the redo stack when a new action is recorded.
    /// </summary>
    void Record(Action undo, Action redo);

    /// <summary>
    /// Executes the next undo action if available.
    /// </summary>
    void Undo();

    /// <summary>
    /// Executes the next redo action if available.
    /// </summary>
    void Redo();

    /// <summary>
    /// Clears both undo and redo stacks.
    /// </summary>
    void Clear();

    /// <summary>
    /// Fires when the undo/redo state changes (CanUndo or CanRedo).
    /// </summary>
    event EventHandler? StateChanged;
}