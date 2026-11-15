namespace MusicLayer.Application.Abstractions;

public interface IAudioPlayer : IAsyncDisposable
{
    event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
}

public sealed class PlaybackEndedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}
