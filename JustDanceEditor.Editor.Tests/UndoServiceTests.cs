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
}