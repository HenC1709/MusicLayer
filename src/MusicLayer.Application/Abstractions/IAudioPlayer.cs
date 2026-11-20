namespace MusicLayer.Application.Abstractions;

public interface IAudioPlayer : IAsyncDisposable
{
    const int DefaultVolumePercent = 100;

    event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> PauseAsync(CancellationToken cancellationToken = default);
    Task<bool> ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default);
    Task<PlaybackProgress?> GetProgressAsync(CancellationToken cancellationToken = default);
}

public readonly record struct PlaybackProgress(TimeSpan Elapsed, TimeSpan Duration);

public sealed class PlaybackEndedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}
