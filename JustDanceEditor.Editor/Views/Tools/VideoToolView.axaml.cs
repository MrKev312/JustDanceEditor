using Avalonia.Controls;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Tools;
using LibVLCSharp.Avalonia;
using System;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class VideoToolView : UserControl
{
    private VideoView? _videoView;

    public VideoToolView()
    {
        InitializeComponent();
        _videoView = this.FindControl<VideoView>("VlcView");

        DataContextChanged += (s, e) => UpdateMediaPlayer();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateMediaPlayer();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_videoView != null)
        {
            _videoView.MediaPlayer = null;
        }

        base.OnUnloaded(e);
    }

    private void UpdateMediaPlayer()
    {
        if (_videoView == null)
            return;

        if (DataContext is VideoToolViewModel vm)
        {
            // Only assign if different to avoid redundant re-attachments
            if (_videoView.MediaPlayer != vm.MediaPlayer)
            {
                _videoView.MediaPlayer = vm.MediaPlayer;
            }
        }
        else
        {
            _videoView.MediaPlayer = null;
        }
    }
}