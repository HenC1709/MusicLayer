using MusicLayer.Application.Abstractions;
using NAudio.Wave;

namespace MusicLayer.Infrastructure.Audio;

public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly object gate = new();
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private string? currentFile;
    private TimeSpan outputStart;
    private bool waitingToResume;
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
                var replacingOutput = output is not null;
                try
                {
                    ReleaseAudio();
                    reader = new MediaFoundationReader(filePath);
                    currentFile = filePath;
                    OpenOutput(reader, play: true);
                }
                catch (Exception error)
                {
                    try { ReleaseAudio(); }
                    finally
                    {
                        // The previous output was detached even if the new track could not start.
                        if (replacingOutput)
                            PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error));
                    }
                    throw;
                }
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
                if (reader is not { } activeReader || output is not { } activeOutput ||
                    (!waitingToResume && activeOutput.PlaybackState != PlaybackState.Paused))
                    return false;

                if (waitingToResume && outputStart == activeReader.TotalTime)
                {
                    CompletePlayback();
                    return false;
                }

                activeOutput.Play();
                waitingToResume = false;
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
                if (reader is null || output is not { } activeOutput ||
                    (!waitingToResume && activeOutput.PlaybackState == PlaybackState.Stopped))
                    return null;

                var duration = reader.TotalTime;
                if (duration <= TimeSpan.Zero)
                    return null;

                // The reader fills buffers ahead of the output. Use the device's played bytes.
                var playedSeconds = waitingToResume ? 0 : (double)activeOutput.GetPosition() /
                    activeOutput.OutputWaveFormat.AverageBytesPerSecond;
                var seconds = outputStart.TotalSeconds + playedSeconds;
                var elapsed = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, duration.TotalSeconds));
                return new PlaybackProgress(elapsed, duration);
            }
        }, cancellationToken);

    public Task<PlaybackProgress?> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        => Task.Run<PlaybackProgress?>(() =>
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (currentFile is not { } file || reader is not { } currentReader || output is not { } currentOutput)
                    return null;

                var state = currentOutput.PlaybackState;
                var paused = waitingToResume || state == PlaybackState.Paused;
                var duration = currentReader.TotalTime;
                if ((!paused && state != PlaybackState.Playing) || duration <= TimeSpan.Zero)
                    return null;

                var target = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
                if (target == duration && !paused)
                {
                    CompletePlayback();
                    return null;
                }

                // Give the new device its own decoder so an outgoing buffer cannot consume the new position.
                // A paused seek to the end completes on resume without reading from the new decoder.
                var replacement = OpenReaderAtPosition(file, target == duration ? TimeSpan.Zero : target, cancellationToken);
                try
                {
                    ReleaseAudio();
                    reader = replacement;
                    currentFile = file;
                    outputStart = target;
                    waitingToResume = paused;
                    OpenOutput(replacement, play: !paused);
                    return new PlaybackProgress(target, duration);
                }
                catch (Exception error)
                {
                    try
                    {
                        if (reader == replacement) ReleaseAudio();
                        else replacement.Dispose();
                    }
                    finally { PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error)); }
                    throw;
                }
            }
        }, cancellationToken);

    internal static MediaFoundationReader OpenReaderAtPosition(string file, TimeSpan target, CancellationToken cancellationToken)
    {
        var source = new MediaFoundationReader(file);
        try
        {
            var bytes = (long)(target.TotalSeconds * source.WaveFormat.AverageBytesPerSecond);
            var offset = bytes - bytes % source.BlockAlign;
            if (offset > 0)
            {
                // Windows can report a successful FLAC seek while decoding from the start.
                if (Path.GetExtension(file).Equals(".flac", StringComparison.OrdinalIgnoreCase))
                    SkipDecodedAudio(source, offset, cancellationToken);
                else
                    source.Position = offset;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static void SkipDecodedAudio(WaveStream source, long remaining, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var chunkSize = buffer.Length - buffer.Length % source.BlockAlign;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, (int)Math.Min(remaining, chunkSize));
            if (read == 0) throw new EndOfStreamException("The audio ended before the requested position.");
            remaining -= read;
        }
    }

    private void OpenOutput(MediaFoundationReader source, bool play)
    {
        output = new WaveOutEvent();
        output.PlaybackStopped += OnStopped;
        output.Init(source);
        output.Volume = volume;
        if (play) output.Play();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // Cleanup runs outside the audio callback; stale callbacks are ignored.
        _ = Task.Run(() =>
        {
            lock (gate)
            {
                if (disposed || !ReferenceEquals(sender, output)) return;
                CompletePlayback(e.Exception);
            }
        });
    }

    private void CompletePlayback(Exception? error = null)
    {
        var completedFile = currentFile;
        try { ReleaseAudio(); }
        catch (Exception cleanupError) { error ??= cleanupError; }
        PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error, completedFile));
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
        currentFile = null;
        outputStart = TimeSpan.Zero;
        waitingToResume = false;
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
