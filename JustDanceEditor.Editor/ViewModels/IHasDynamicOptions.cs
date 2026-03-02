using JustDanceEditor.Editor.ViewModels.Timeline;

using System.Collections.Generic;

namespace JustDanceEditor.Editor.ViewModels;

/// <summary>
/// Implemented by clip view-models that supply a dynamic drop-down list of valid
/// values for one or more of their inspectable properties (e.g. MoveId, PictogramId).
/// </summary>
public interface IHasDynamicOptions
{
    /// <summary>
    /// Returns the option objects for <paramref name="propertyName"/>, or
    /// <c>null</c> if this clip does not provide dynamic options for that property.
    /// </summary>
    IEnumerable<object>? GetDynamicOptions(string propertyName, TimelineEditorViewModel timeline);

    /// <summary>
    /// Returns whether the user may type a free-form value in addition to the
    /// provided list for <paramref name="propertyName"/>.
    /// </summary>
    bool IsDynamicPropertyEditable(string propertyName);
}
