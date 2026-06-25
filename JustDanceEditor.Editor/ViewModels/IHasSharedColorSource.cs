using JustDanceEditor.Editor.ViewModels.Timeline;

namespace JustDanceEditor.Editor.ViewModels;

/// <summary>
/// Implemented by clip view-models whose "BackgroundColor" UI property should
/// redirect all editing to a <em>shared</em>, canonical source-of-truth object
/// (e.g. a <c>MoveDefinitionViewModel</c> or the timeline's lyrics-color field)
/// rather than to the per-clip property directly.
/// </summary>
public interface IHasSharedColorSource
{
    /// <summary>
    /// Returns the (Target, PropertyName) pair that the Properties pane should
    /// bind to when editing <paramref name="inspectedProperty"/>, or <c>null</c>
    /// if this clip does not redirect that property.
    /// </summary>
    (object Target, string PropertyName)? GetColorEditTarget(
        string inspectedProperty,
        TimelineEditorViewModel timeline);
}