using MusicLayer.Application.Abstractions;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class ResumePlayback(IAudioPlayer player)
{
    public Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
        => player.ResumeAsync(cancellationToken);
}
