using Avalonia.Controls;

using JustDanceEditor.Editor.ViewModels.Tools;

using LibVLCSharp.Avalonia;

using System.ComponentModel;

namespace JustDanceEditor.Editor.Views.Tools;

public partial class VideoToolView : UserControl
{
    private readonly VideoView? _videoView;
    private VideoToolViewModel? _subscribedVm;

    public VideoToolView()
    {
        InitializeComponent();
        _videoView = this.FindControl<VideoView>("VlcView");

        DataContextChanged += (s, e) =>
        {
            _subscribedVm?.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;

            if (DataContext is VideoToolViewModel vm)
            {
                _subscribedVm = vm;
                vm.PropertyChanged += OnVmPropertyChanged;
            }

            UpdateMediaPlayer();
        };
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateMediaPlayer();
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        _videoView?.MediaPlayer = null;
        _subscribedVm?.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = null;

        base.OnUnloaded(e);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoToolViewModel.MediaPlayer))
            UpdateMediaPlayer();
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