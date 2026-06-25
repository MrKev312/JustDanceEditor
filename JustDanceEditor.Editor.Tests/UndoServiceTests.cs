using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.Tests;

public class UndoServiceTests
{
    [Fact]
    public void Record_Undo_Redo_Workflow()
    {
        UndoService svc = new();
        int x = 0;

        svc.Record(() => x--, () => x++);
        Assert.True(svc.CanUndo);
        Assert.False(svc.CanRedo);

        svc.Undo();
        Assert.Equal(-1, x);
        Assert.False(svc.CanUndo);
        Assert.True(svc.CanRedo);

        svc.Redo();
        Assert.Equal(0, x);
        Assert.True(svc.CanUndo);
        Assert.False(svc.CanRedo);
    }

    [Fact]
    public void Clear_Empties_Stacks_And_Fires_StateChanged()
    {
        UndoService svc = new();
        bool fired = false;
        svc.StateChanged += (s, e) => fired = true;

        svc.Record(() => { }, () => { });
        Assert.True(svc.CanUndo);

        svc.Clear();
        Assert.False(svc.CanUndo);
        Assert.False(svc.CanRedo);
        Assert.True(fired);
    }

    [Fact]
    public void Undo_Noop_When_NoUndo()
    {
        UndoService svc = new();
        svc.Undo(); // should be a no-op
        svc.Redo(); // noop
        Assert.False(svc.CanUndo);
        Assert.False(svc.CanRedo);
    }

    // -------------------------------------------------------------------
    // IsDirty / MarkSaved tests
    // -------------------------------------------------------------------

    [Fact]
    public void IsDirty_FalseInitially()
    {
        UndoService svc = new();
        Assert.False(svc.IsDirty);
    }

    [Fact]
    public void IsDirty_TrueAfterRecord_FalseAfterMarkSaved()
    {
        UndoService svc = new();
        svc.Record(() => { }, () => { });
        Assert.True(svc.IsDirty);

        svc.MarkSaved();
        Assert.False(svc.IsDirty);
    }

    [Fact]
    public void IsDirty_TrueAfterUndo_FalseAfterRedo_WhenSavedAtTop()
    {
        UndoService svc = new();
        svc.Record(() => { }, () => { });
        svc.MarkSaved(); // save with 1 item in undo stack

        svc.Undo();       // move back past saved position
        Assert.True(svc.IsDirty);

        svc.Redo();       // restore to saved position
        Assert.False(svc.IsDirty);
    }

    [Fact]
    public void IsDirty_CorrectAfterSaveUndoNewAction_SameStackDepth()
    {
        // Regression: save, undo, new action — stack depth equals saved depth
        // but history branch differs, so IsDirty must still be true.
        UndoService svc = new();
        svc.Record(() => { }, () => { }); // action A (seq 1)
        svc.MarkSaved();                  // savepoint = seq 1

        svc.Undo();                       // back to empty (seq 0) — dirty
        Assert.True(svc.IsDirty);

        svc.Record(() => { }, () => { }); // action C (seq 2) — same stack depth=1 as at save
        Assert.True(svc.IsDirty);         // must still be dirty (seq 2 != savepoint seq 1)
    }

    [Fact]
    public void MarkSaved_AtCurrentPosition_ClearsDirty_AfterUndo()
    {
        // Save at an undone position: dirty should clear.
        UndoService svc = new();
        svc.Record(() => { }, () => { });
        svc.Record(() => { }, () => { });
        svc.Undo();      // undo to one item in stack
        svc.MarkSaved(); // save here
        Assert.False(svc.IsDirty);

        svc.Redo();      // move forward — dirty again
        Assert.True(svc.IsDirty);

        svc.Undo();      // back to saved position
        Assert.False(svc.IsDirty);
    }

    [Fact]
    public void Clear_MakesDirty_WhenSavepointWasNonZero()
    {
        UndoService svc = new();
        svc.Record(() => { }, () => { });
        svc.MarkSaved(); // savepoint = seq 1

        svc.Clear();     // resets topSequence to 0, savepoint stays at seq 1
        Assert.True(svc.IsDirty);
    }
}