using MusicLayer.Application.Abstractions;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class SetPlaybackVolume(IAudioPlayer player)
{
    public Task ExecuteAsync(int percent, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return player.SetVolumeAsync(percent, cancellationToken);
    }
}
