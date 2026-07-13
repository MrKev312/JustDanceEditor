using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public partial class PropertyItemViewModel : ObservableObject, IDisposable
{
    private readonly List<object> _targets;
    private readonly IInspectablePropertyDescriptor _descriptor;
    private readonly IUndoService _undoService;
    private bool _isColorPickerActive = false;
    private object? _colorPickerInitialValue;

    /// <summary>
    /// True while the Value setter is iterating through targets. Suppresses
    /// <see cref="Target_PropertyChanged"/> to avoid intermediate binding
    /// reads that see an inconsistent mix of old/new values across targets.
    /// </summary>
    private bool _isBatchSetting;

    // Direct subscriptions for INotifyPropertyChanged targets (unsubscribed in Dispose)
    private readonly List<INotifyPropertyChanged> _inpcSubscriptions = [];

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBatchSetting)
            return;

        if (e.PropertyName == _descriptor.NotificationPropertyName)
        {
            // Refresh bound value when underlying property changed
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ColorValue)));
        }
    }

    private record ColorSnapshot(bool IsLyrics, List<(ClipViewModel Clip, Color Color)> ClipColors, string? OldMetadata, List<Color>? FinalColors = null);
    private record MoveDefinitionsSnapshot(List<(MoveDefinitionViewModel Def, Color Color)> DefColors);

    public string Name { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTextBox))]
    [NotifyPropertyChangedFor(nameof(ShowSlider))]
    [NotifyPropertyChangedFor(nameof(ShowComboBox))]
    public partial System.Collections.IEnumerable? Options { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; set; } = true;

    public double Minimum { get; }
    public double Maximum { get; } = 1.0;
    public double TickFrequency { get; } = 0.1;
    public bool HasSliderRange { get; }

    public PropertyItemViewModel(
        List<object> targets,
        IInspectablePropertyDescriptor descriptor,
        IUndoService undoService)
    {
        if (targets.Count == 0)
            throw new ArgumentException("At least one property target is required.", nameof(targets));

        _targets = targets;
        _descriptor = descriptor;
        _undoService = undoService;
        Name = descriptor.DisplayName;
        IsReadOnly = descriptor.IsReadOnly;
        if (descriptor.NumericRange is { } numeric)
        {
            Minimum = numeric.Minimum;
            Maximum = numeric.Maximum;
            TickFrequency = numeric.TickFrequency;
            HasSliderRange = true;
        }

        foreach (object target in _targets)
        {
            if (target is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += Target_PropertyChanged;
                _inpcSubscriptions.Add(inpc);
            }
        }
    }

    /// <summary>
    /// Notifies that the color picker has opened.
    /// </summary>
    public void OnColorPickerOpened()
    {
        _isColorPickerActive = true;
        _colorPickerInitialValue = null;

        // Shared move colors resolve to their canonical definition targets.
        if (_targets.Count > 0 && _targets[0] is MoveDefinitionViewModel)
        {
            List<(MoveDefinitionViewModel Def, Color Color)> list =
                [.. _targets.OfType<MoveDefinitionViewModel>().Select(d => (d, d.Color))];
            _colorPickerInitialValue = new MoveDefinitionsSnapshot(list);
        }
        // If editing a ClipViewModel's BackgroundColor, capture a full snapshot
        else if (_targets.Count > 0 && _targets[0] is ClipViewModel cv)
        {
            // Lyrics: capture all lyrics clips and metadata
            if (cv is KaraokeClipViewModel)
            {
                // For lyrics, target the timeline-level lyrics definition. The property item
                // will be created bound to that definition, so we don't capture per-clip state here.
                List<(ClipViewModel c, Color BackgroundColor)> selected = [.. _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor))];
                ColorSnapshot snapshot = new(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
            else if (cv is MoveClipViewModel)
            {
                // For moves, capture the unique move definitions (not per-clip colors)
                List<MoveDefinitionViewModel> defs = [.. _targets.OfType<MoveClipViewModel>().Select(m => m.Definition).OfType<MoveDefinitionViewModel>().Distinct()];
                List<(MoveDefinitionViewModel Def, Color Color)> list = [.. defs.Select(d => (d, d.Color))];
                MoveDefinitionsSnapshot snapshot = new(list);
                _colorPickerInitialValue = snapshot;
            }
            else
            {
                List<(ClipViewModel c, Color BackgroundColor)> selected = [.. _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor))];
                ColorSnapshot snapshot = new(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
        }
        else
        {
            // Fallback: store per-target initial values
            _colorPickerInitialValue = _targets.Select(t => (t, GetValue(t))).ToList();
        }
    }

    /// <summary>
    /// Notifies that the color picker has closed. This pushes the change to the undo stack.
    /// </summary>
    public void OnColorPickerClosed()
    {
        // Keep _isColorPickerActive true until we've recorded the grouped undo to avoid the ColorValue binding
        // triggering an extra undo when the binding finalizes after the picker loses focus.
        try
        {
            // If we have a MoveDefinitionsSnapshot (for moves), push undo/redo at the definition level
            if (_colorPickerInitialValue is MoveDefinitionsSnapshot moveSnap)
            {
                List<Color> initial = [.. moveSnap.DefColors.Select(d => d.Color)];
                List<MoveDefinitionViewModel> defs = [.. moveSnap.DefColors.Select(d => d.Def)];
                List<Color> final = [.. defs.Select(d => new Color(255, d.Color.R, d.Color.G, d.Color.B))];

                bool anyChanged = initial.Where((v, i) => !Equals(v, final[i])).Any();

                if (anyChanged)
                {
                    string initialHex = initial.Count > 0 ? ClipViewModel.ColorToRgbaHex(initial[0]) : "null";
                    string finalHex = final.Count > 0 ? ClipViewModel.ColorToRgbaHex(final[0]) : "null";
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < defs.Count; i++)
                                defs[i].Color = initial[i];
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < defs.Count; i++)
                                defs[i].Color = final[i];
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // If the picker was editing the lyrics color at the timeline level, record a single
            // undo that updates the timeline metadata only. We no longer set per-clip BackgroundColor
            // during undo/redo to avoid duplicate or conflicting undo entries.

            // If we have a ColorSnapshot (non-lyrics), push a single composite undo/redo
            if (_colorPickerInitialValue is ColorSnapshot snap)
            {
                // Non-lyrics: use the captured list to create undo/redo per captured clip
                List<Color> initialList = [.. snap.ClipColors.Select(cc => cc.Color)];
                List<ClipViewModel> clips = [.. snap.ClipColors.Select(cc => cc.Clip)];
                List<Color> finalList = [.. clips.Select(c => c.BackgroundColor)];

                bool anyChanged = initialList.Where((v, i) => !Equals(v, finalList[i])).Any();

                if (anyChanged)
                {
                    string initialHex = initialList.Count > 0 ? ClipViewModel.ColorToRgbaHex(initialList[0]) : "null";
                    string finalHex = finalList.Count > 0 ? ClipViewModel.ColorToRgbaHex(finalList[0]) : "null";
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = initialList[i];
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = finalList[i];
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // Fallback behavior when we captured per-target initial values
            if (_colorPickerInitialValue is List<(object Target, object? Value)> perTarget)
            {
                List<object?> initialList = [.. perTarget.Select(p => p.Value)];
                List<object> targets = [.. perTarget.Select(p => p.Target)];
                List<object?> finalList = [.. targets.Select(GetValue)];

                bool anyChanged = initialList.Where((v, i) => !Equals(v, finalList[i])).Any();

                if (anyChanged)
                {
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < targets.Count; i++)
                                SetValue(targets[i], initialList[i]);
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < targets.Count; i++)
                                SetValue(targets[i], finalList[i]);
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // Legacy scalar fallback (single target, non-snapshot)
            object? finalValue = GetValue(_targets[0]);
            if (!Equals(_colorPickerInitialValue, finalValue))
            {
                object? capturedInitial = _colorPickerInitialValue;
                _undoService.Record(
                    undo: () => SetValue(_targets[0], capturedInitial),
                    redo: () => SetValue(_targets[0], finalValue)
                );
            }

            _colorPickerInitialValue = null;
        }
        finally
        {
            _isColorPickerActive = false;
        }
    }

    public object? Value
    {
        get
        {
            // Return value if all are same, else null
            object? firstVal = GetValue(_targets[0]);
            for (int i = 1; i < _targets.Count; i++)
            {
                if (!Equals(GetValue(_targets[i]), firstVal))
                    return null;
            }

            return firstVal;
        }
        set
        {
            if (value == null || !CanWrite)
                return; // Don't set nulls explicitly (e.g. from empty selection)

            List<object?> oldValues = [.. _targets.Select(GetValue)];

            // Only push undo if not in color picker mode and the value actually changed
            if (!_isColorPickerActive)
            {
                bool anyDifferent = oldValues.Any(v => !Equals(v, value));

                if (anyDifferent)
                {
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < _targets.Count; i++)
                                SetValue(_targets[i], oldValues[i]);
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < _targets.Count; i++)
                                SetValue(_targets[i], value);
                        }
                    );
                }
            }

            _isBatchSetting = true;
            try
            {
                foreach (object target in _targets)
                {
                    SetValue(target, value);
                }
            }
            finally
            {
                _isBatchSetting = false;
            }

            OnPropertyChanged(nameof(Value));
        }
    }

    private object? GetValue(object target) => _descriptor.GetValue(target);
    private void SetValue(object target, object? value) => _descriptor.SetValue(target, value);

    public Type PropertyType => _descriptor.PropertyType;
    public bool CanWrite => !IsReadOnly;

    // Binding Helpers
    public bool IsColor => PropertyType == typeof(Color);
    public bool IsBool => PropertyType == typeof(bool);
    public bool IsStringOrNumber => PropertyType == typeof(string) || IsNumber;
    public bool IsNumber => PropertyType == typeof(double) || PropertyType == typeof(int) || PropertyType == typeof(float);
    public bool HasOptions => Options != null;
    public bool ShowSlider => IsNumber && !HasOptions && HasSliderRange && CanWrite;
    public bool ShowReadOnlyText => IsReadOnly && !IsBool && !IsColor;
    public bool ShowTextBox => IsStringOrNumber && !HasOptions && !ShowSlider && !ShowReadOnlyText;
    public bool ShowComboBox => HasOptions && !ShowReadOnlyText;
    public string SliderRangeText => $"{FormatNumber(Minimum)} to {FormatNumber(Maximum)}";
    public string DisplayValue => FormatValue(Value);

    public string StringValue
    {
        get => Value?.ToString() ?? "";
        set
        {
            if (IsBinding || !CanWrite)
                return;
            try
            {
                IsBinding = true;
                if (PropertyType == typeof(string))
                    Value = value;
                else if (PropertyType == typeof(double) && double.TryParse(value, out double d))
                    Value = d;
                else if (PropertyType == typeof(int) && int.TryParse(value, out int i))
                    Value = i;
                else if (PropertyType == typeof(float) && float.TryParse(value, out float f))
                    Value = f;
            }
            finally
            {
                IsBinding = false;
            }
        }
    }

    public double NumericValue
    {
        get
        {
            object? value = Value;
            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                _ => 0.0
            };
        }
        set
        {
            if (IsBinding || !CanWrite || !ShowSlider)
                return;

            try
            {
                IsBinding = true;
                if (PropertyType == typeof(double))
                    Value = value;
                else if (PropertyType == typeof(float))
                    Value = (float)value;
                else if (PropertyType == typeof(int))
                    Value = (int)Math.Round(value);
            }
            finally
            {
                IsBinding = false;
            }
        }
    }

    public bool BoolValue
    {
        get => Value is bool b && b;
        set
        {
            if (CanWrite)
                Value = value;
        }
    }

    public Color ColorValue
    {
        get => Value is Color c ? c : Colors.Transparent;
        set
        {
            if (CanWrite)
                Value = value;
        }
    }

    public object? SelectedOption
    {
        get
        {
            if (Options == null)
                return null;
            string currentVal = StringValue;
            foreach (object? opt in Options)
            {
                if (opt is PictogramOptionViewModel p && p.Name == currentVal)
                    return opt;
                if (opt is string s && s == currentVal)
                    return opt;
            }

            return null;
        }
        set
        {
            if (!CanWrite)
                return;

            if (value is PictogramOptionViewModel p)
                StringValue = p.Name;
            else if (value != null)
                StringValue = value.ToString() ?? "";
        }
    }

    public string HexString
    {
        get => Value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}" : "";
        set
        {
            if (!CanWrite)
                return;

            // Try parsing as RGBA hex first (our format), then fall back to standard parsing
            if (!string.IsNullOrEmpty(value))
            {
                if (_descriptor.ColorEncoding == PropertyColorEncoding.Rgba)
                {
                    // Use RGBA parsing for lyrics
                    ColorValue = ClipViewModel.ParseRgbaHex(value);
                }
                else
                {
                    // For other clips, use standard ARGB parsing with full alpha
                    if (Color.TryParse(value, out Color c))
                    {
                        ColorValue = new Color(255, c.R, c.G, c.B);
                    }
                }
            }
        }
    }

    private bool IsBinding = false;

    public void Dispose()
    {
        foreach (INotifyPropertyChanged inpc in _inpcSubscriptions)
            inpc.PropertyChanged -= Target_PropertyChanged;

        _inpcSubscriptions.Clear();
        if (Options != null)
        {
            foreach (object? option in Options)
            {
                if (option is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    // Refresh typed properties when Value changes
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Value))
        {
            OnPropertyChanged(nameof(StringValue));
            OnPropertyChanged(nameof(NumericValue));
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(ColorValue));
            OnPropertyChanged(nameof(HexString));
            OnPropertyChanged(nameof(SelectedOption));
        }
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => string.Empty,
            double d => FormatNumber(d),
            float f => FormatNumber(f),
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Color c => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}",
            _ => value.ToString() ?? string.Empty
        };

    private static string FormatNumber(double value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatNumber(float value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}