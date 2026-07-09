using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public sealed record TimelineEditorServices(
    ITimelineContextService? TimelineContext = null,
    IDialogService? Dialogs = null,
    IWindowService? Windows = null,
    IEditorPromptService? Prompts = null)
{
    public static TimelineEditorServices Detached { get; } = new();
}
