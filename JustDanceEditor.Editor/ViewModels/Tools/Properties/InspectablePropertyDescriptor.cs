using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public readonly record struct NumericPropertyRange(double Minimum, double Maximum, double TickFrequency);

public enum PropertyColorEncoding
{
    Argb,
    Rgba
}

public interface IInspectablePropertyDescriptor
{
    string Key { get; }
    string NotificationPropertyName { get; }
    string DisplayName { get; }
    string Category { get; }
    Type PropertyType { get; }
    bool IsReadOnly { get; }
    NumericPropertyRange? NumericRange { get; }
    PropertyColorEncoding ColorEncoding { get; }

    object? GetValue(object target);
    void SetValue(object target, object? value);
    ResolvedInspectableProperty? TryResolve(
        IReadOnlyList<object> selection,
        TimelineEditorViewModel timeline);
}

public sealed record ResolvedInspectableProperty(
    IInspectablePropertyDescriptor Descriptor,
    IReadOnlyList<object> Targets,
    IReadOnlyList<object>? Options,
    bool IsEditable);

public sealed class InspectablePropertyDescriptor<TTarget, TValue> : IInspectablePropertyDescriptor
    where TTarget : class
{
    private readonly Func<TTarget, TValue> _getter;
    private readonly Action<TTarget, TValue>? _setter;
    private readonly Func<TTarget, TimelineEditorViewModel, IEnumerable<object>?>? _options;

    public InspectablePropertyDescriptor(
        string key,
        string displayName,
        string category,
        Func<TTarget, TValue> getter,
        Action<TTarget, TValue>? setter = null,
        NumericPropertyRange? numericRange = null,
        Func<TTarget, TimelineEditorViewModel, IEnumerable<object>?>? options = null,
        bool isEditable = true,
        PropertyColorEncoding colorEncoding = PropertyColorEncoding.Argb)
    {
        Key = key;
        NotificationPropertyName = key;
        DisplayName = displayName;
        Category = category;
        _getter = getter;
        _setter = setter;
        NumericRange = numericRange;
        _options = options;
        IsEditable = isEditable;
        ColorEncoding = colorEncoding;
    }

    public string Key { get; }
    public string NotificationPropertyName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public Type PropertyType => typeof(TValue);
    public bool IsReadOnly => _setter == null;
    public NumericPropertyRange? NumericRange { get; }
    public bool IsEditable { get; }
    public PropertyColorEncoding ColorEncoding { get; }

    public object? GetValue(object target) => _getter(RequireTarget(target));

    public void SetValue(object target, object? value)
    {
        if (_setter == null)
            return;

        if (value is not TValue typedValue)
            throw new ArgumentException($"Expected a value of type {typeof(TValue).FullName}.", nameof(value));

        _setter(RequireTarget(target), typedValue);
    }

    public ResolvedInspectableProperty? TryResolve(
        IReadOnlyList<object> selection,
        TimelineEditorViewModel timeline)
    {
        if (selection.Count == 0 || selection.Any(static target => target is not TTarget))
            return null;

        List<object> targets = [.. selection.Cast<TTarget>().Distinct().Cast<object>()];
        IReadOnlyList<object>? options = _options?.Invoke((TTarget)selection[0], timeline)?.ToList();
        return new(this, targets, options, IsEditable);
    }

    private static TTarget RequireTarget(object target)
        => target as TTarget
            ?? throw new ArgumentException($"Expected a target of type {typeof(TTarget).FullName}.", nameof(target));
}

internal sealed class MappedInspectablePropertyDescriptor<TSelection, TTarget, TValue> : IInspectablePropertyDescriptor
    where TSelection : class
    where TTarget : class
{
    private readonly Func<TSelection, TimelineEditorViewModel, TTarget?> _targetResolver;
    private readonly Func<TTarget, TValue> _getter;
    private readonly Action<TTarget, TValue>? _setter;

    public MappedInspectablePropertyDescriptor(
        string key,
        string notificationPropertyName,
        string displayName,
        string category,
        Func<TSelection, TimelineEditorViewModel, TTarget?> targetResolver,
        Func<TTarget, TValue> getter,
        Action<TTarget, TValue>? setter,
        PropertyColorEncoding colorEncoding = PropertyColorEncoding.Argb)
    {
        Key = key;
        NotificationPropertyName = notificationPropertyName;
        DisplayName = displayName;
        Category = category;
        _targetResolver = targetResolver;
        _getter = getter;
        _setter = setter;
        ColorEncoding = colorEncoding;
    }

    public string Key { get; }
    public string NotificationPropertyName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public Type PropertyType => typeof(TValue);
    public bool IsReadOnly => _setter == null;
    public NumericPropertyRange? NumericRange => null;
    public PropertyColorEncoding ColorEncoding { get; }

    public object? GetValue(object target) => _getter(RequireTarget(target));

    public void SetValue(object target, object? value)
    {
        if (_setter == null)
            return;

        if (value is not TValue typedValue)
            throw new ArgumentException($"Expected a value of type {typeof(TValue).FullName}.", nameof(value));

        _setter(RequireTarget(target), typedValue);
    }

    public ResolvedInspectableProperty? TryResolve(
        IReadOnlyList<object> selection,
        TimelineEditorViewModel timeline)
    {
        if (selection.Count == 0 || selection.Any(static target => target is not TSelection))
            return null;

        List<object> targets = [];
        foreach (TSelection selected in selection.Cast<TSelection>())
        {
            TTarget? target = _targetResolver(selected, timeline);
            if (target == null)
                return null;
            if (!targets.Contains(target, ReferenceEqualityComparer.Instance))
                targets.Add(target);
        }

        return new(this, targets, null, true);
    }

    private static TTarget RequireTarget(object target)
        => target as TTarget
            ?? throw new ArgumentException($"Expected a target of type {typeof(TTarget).FullName}.", nameof(target));
}