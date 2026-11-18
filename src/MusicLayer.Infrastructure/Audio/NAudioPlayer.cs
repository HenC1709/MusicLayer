using MusicLayer.Application.Abstractions;
using NAudio.Wave;

namespace MusicLayer.Infrastructure.Audio;

public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly object gate = new();
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private bool disposed;
    public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseAudio();
                try
                {
                    reader = new MediaFoundationReader(filePath);
                    output = new WaveOutEvent();
                    output.PlaybackStopped += OnStopped;
                    output.Init(reader);
                    output.Play();
                }
                catch { ReleaseAudio(); throw; }
            }
        }, cancellationToken);

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (output is not { PlaybackState: PlaybackState.Playing } activeOutput)
                    return false;

                activeOutput.Pause();
                return true;
            }
        }, cancellationToken);

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (output is not { PlaybackState: PlaybackState.Paused } activeOutput)
                    return false;

                activeOutput.Play();
                return true;
            }
        }, cancellationToken);

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // Cleanup runs outside the audio callback; stale callbacks are ignored.
        _ = Task.Run(() =>
        {
            lock (gate)
            {
                if (disposed || !ReferenceEquals(sender, output)) return;
                Exception? error = e.Exception;
                try { ReleaseAudio(); }
                catch (Exception cleanupError) { error ??= cleanupError; }
                PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error));
            }
        });
    }

    public ValueTask DisposeAsync() => new(Task.Run(() =>
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            ReleaseAudio();
        }
    }));

    private void ReleaseAudio()
    {
        var oldOutput = output;
        var oldReader = reader;
        output = null;
        reader = null;
        try
        {
            if (oldOutput is not null)
            {
                oldOutput.PlaybackStopped -= OnStopped;
                try { oldOutput.Stop(); }
                finally { oldOutput.Dispose(); }
            }
        }
        finally { oldReader?.Dispose(); }
    }
}
