using MusicLayer.Application.Abstractions;
using NAudio.Wave;

namespace MusicLayer.Infrastructure.Audio;

public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly object gate = new();
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private float volume = IAudioPlayer.DefaultVolumePercent / 100f;
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
                    output.Volume = volume;
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

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseAudio();
            }
        }, cancellationToken);

    public Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                var newVolume = percent / 100f;
                if (output is not null)
                    output.Volume = newVolume;

                volume = newVolume;
            }
        }, cancellationToken);

    public Task<PlaybackProgress?> GetProgressAsync(CancellationToken cancellationToken = default)
        => Task.Run<PlaybackProgress?>(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (reader is null || output is not { PlaybackState: not PlaybackState.Stopped } activeOutput)
                    return null;

                var duration = reader.TotalTime;
                if (duration <= TimeSpan.Zero)
                    return null;

                // The reader fills buffers ahead of the output. Use the device's played bytes.
                var seconds = (double)activeOutput.GetPosition() / activeOutput.OutputWaveFormat.AverageBytesPerSecond;
                var elapsed = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, duration.TotalSeconds));
                return new PlaybackProgress(elapsed, duration);
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
