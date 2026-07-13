using JustDanceEditor.Editor.Services;

using System;
using System.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineSnapSettingsController : IDisposable
{
    private readonly TimelineEditorViewModel _timeline;
    private readonly TimelineSettingsService _settings;
    private int _suppressBroadcast;

    public TimelineSnapSettingsController(TimelineEditorViewModel timeline, TimelineSettingsService settings)
    {
        _timeline = timeline;
        _settings = settings;
        _settings.PropertyChanged += Settings_PropertyChanged;
    }

    public void Initialize()
    {
        ApplyRemoteSettings();
    }

    public void SetSnapToGrid(bool value)
    {
        if (_suppressBroadcast > 0)
            return;

        _settings.SnapToGrid = value;
    }

    public void SetSnapToCurrentTimeMarker(bool value)
    {
        if (_suppressBroadcast > 0)
            return;

        _settings.SnapToCurrentTimeMarker = value;
    }

    public void SetSnapGridSize(double value)
    {
        if (_suppressBroadcast > 0)
            return;

        _settings.SnapGridSize = value;
    }

    public void SetSnapThreshold(double value)
    {
        if (_suppressBroadcast > 0)
            return;

        _settings.SnapThreshold = value;
    }

    public void SetSnapToClips(bool value)
    {
        if (_suppressBroadcast > 0)
            return;

        _settings.SnapToClips = value;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyRemoteSettings(e.PropertyName);
    }

    private void ApplyRemoteSettings(string? changedProperty = null)
    {
        _suppressBroadcast++;
        try
        {
            if (changedProperty is null or (nameof(TimelineSettingsService.SnapToGrid)))
                _timeline.SnapToGrid = _settings.SnapToGrid;

            if (changedProperty is null or (nameof(TimelineSettingsService.SnapToCurrentTimeMarker)))
                _timeline.SnapToCurrentTimeMarker = _settings.SnapToCurrentTimeMarker;

            if (changedProperty is null or (nameof(TimelineSettingsService.SnapGridSize)))
                _timeline.SnapGridSize = _settings.SnapGridSize;

            if (changedProperty is null or (nameof(TimelineSettingsService.SnapThreshold)))
                _timeline.SnapThreshold = _settings.SnapThreshold;

            if (changedProperty is null or (nameof(TimelineSettingsService.SnapToClips)))
                _timeline.SnapToClips = _settings.SnapToClips;
        }
        finally
        {
            _suppressBroadcast--;
        }
    }

    public void Dispose()
        => _settings.PropertyChanged -= Settings_PropertyChanged;
}