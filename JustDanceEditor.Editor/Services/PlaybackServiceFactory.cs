namespace JustDanceEditor.Editor.Services;

internal sealed class PlaybackServiceFactory(IWindowService windows) : IPlaybackServiceFactory
{
    public IPlaybackService Create() => new PlaybackService(windows);
}