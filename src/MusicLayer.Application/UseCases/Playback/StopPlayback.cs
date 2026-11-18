using MusicLayer.Application.Abstractions;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class StopPlayback(IAudioPlayer player)
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
        => player.StopAsync(cancellationToken);
}
