using MusicLayer.Application.Abstractions;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class PausePlayback(IAudioPlayer player)
{
    public Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
        => player.PauseAsync(cancellationToken);
}
