using Avalonia.Threading;

using Dock.Model.Core;

using JustDanceEditor.Editor.Services;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineCloseController(TimelineEditorViewModel timeline, Func<string> getBaseTitle)
{
    private bool _allowClose;

    public bool ShouldClose()
    {
        if (timeline.UndoService.IsDirty && !_allowClose)
        {
            Dispatcher.UIThread.InvokeAsync(PromptSaveOnCloseAsync);
            return false;
        }

        timeline.Dispose();
        return true;
    }

    public async Task<bool> TrySaveAndReportFailureAsync()
    {
        try
        {
            timeline.Save();
            return true;
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Save map");
            if (timeline.Services.Prompts != null)
            {
                await timeline.Services.Prompts.ShowErrorAsync(
                    "Error Saving Map",
                    $"Failed to save map:\n{ex.Message}",
                    ex);
            }
            return false;
        }
    }

    public async Task<bool> TrySaveAndCloseAfterPromptAsync()
    {
        if (!await TrySaveAndReportFailureAsync())
            return false;

        CloseAfterConfirmedPrompt();
        return true;
    }

    private void CloseAfterConfirmedPrompt()
    {
        _allowClose = true;
        (timeline.Owner as IDock)?.Factory?.CloseDockable(timeline);
    }

    private async Task PromptSaveOnCloseAsync()
    {
        IEditorPromptService? prompts = timeline.Services.Prompts;
        if (prompts == null)
            return;

        UnsavedChangesChoice choice = await prompts.PromptUnsavedChangesAsync(getBaseTitle());
        if (choice == UnsavedChangesChoice.Save)
        {
            await TrySaveAndCloseAfterPromptAsync();
            return;
        }

        if (choice == UnsavedChangesChoice.Discard)
            CloseAfterConfirmedPrompt();
    }
}
