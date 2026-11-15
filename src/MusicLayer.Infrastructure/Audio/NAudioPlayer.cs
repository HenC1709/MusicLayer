using MusicLayer.Application.Abstractions;
using NAudio.Wave;

namespace MusicLayer.Infrastructure.Audio;

public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WaveOutEvent? output;
    private MediaFoundationReader? reader;
    private bool disposed;

    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseAudio();
                try
                {
                    reader = new MediaFoundationReader(filePath);
                    output = new WaveOutEvent();
                    output.Init(reader);
                    output.Play();
                }
                catch
                {
                    ReleaseAudio();
                    throw;
                }
            }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            await Task.Run(ReleaseAudio);
        }
        finally
        {
            gate.Release();
        }
    }

    private void ReleaseAudio()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
        reader?.Dispose();
        reader = null;
    }
}
