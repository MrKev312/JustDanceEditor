using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using JustDanceEditor.Editor.Views.Timeline;

using System.Reflection;

namespace JustDanceEditor.Editor.Tests;

public class TimelineTrackPanelTests
{
    private static PropertyInfo GetCurrentContextMenuProperty()
    {
        return typeof(TimelineTrackPanel)
            .GetProperty("CurrentContextMenu", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(TimelineTrackPanel).FullName, "CurrentContextMenu");
    }

    private class TestMenu : ContextMenu
    {
        public bool CloseCalled { get; private set; }

        public override void Close()
        {
            CloseCalled = true;
            base.Close();
        }
    }

    [AvaloniaFact]
    public void OpenAddClipMenu_ClosesPreviousMenu()
    {
        // arrange: stub out an existing menu and verify close is invoked
        TimelineTrackPanel panel = new();
        TestMenu stub = new();

        // use reflection to bypass the private setter on the public property
        PropertyInfo prop = GetCurrentContextMenuProperty();
        prop.SetValue(null, stub);

        // act
        panel.OpenAddClipMenu(null);

        // assert
        Assert.True(stub.CloseCalled);
        Assert.NotSame(stub, TimelineTrackPanel.CurrentContextMenu);
    }

    [AvaloniaFact]
    public void OpenAddClipMenu_ReplacesCurrentMenu()
    {
        TimelineTrackPanel panel = new();

        panel.OpenAddClipMenu(null);
        ContextMenu? first = TimelineTrackPanel.CurrentContextMenu;
        Assert.NotNull(first);

        panel.OpenAddClipMenu(null);
        ContextMenu? second = TimelineTrackPanel.CurrentContextMenu;

        Assert.NotSame(first, second);
    }
}
