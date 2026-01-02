namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Implement to expose a runnable command that can be invoked from the dynamic menu.
/// </summary>
public interface IRunCommand
{
    /// <summary>
    /// Execute the command. The timeline context may be null when no timeline is open.
    /// </summary>
    /// <param name="timelineContext">The current timeline context.</param>
    void Run(ITimelineContextService? timelineContext);

    /// <summary>
    /// Return whether the command can currently be executed. Used to enable/disable (grey out)
    /// the corresponding menu item. The timeline context may be null when no timeline is open.
    /// </summary>
    /// <param name="timelineContext">The current timeline context.</param>
    /// <returns>True if the command may run, false to disable it.</returns>
    bool CanRun(ITimelineContextService? timelineContext);
}