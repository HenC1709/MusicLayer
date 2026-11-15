namespace MusicLayer.Application.Abstractions;

public interface IAudioPlayer : IAsyncDisposable
{
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
}
