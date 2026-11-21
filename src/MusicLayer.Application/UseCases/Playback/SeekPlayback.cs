using MusicLayer.Application.Abstractions;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class SeekPlayback(IAudioPlayer player)
{
    public Task<PlaybackProgress?> ExecuteAsync(double fraction, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(fraction) || duration <= TimeSpan.Zero)
            return Task.FromResult<PlaybackProgress?>(null);

        var position = TimeSpan.FromTicks((long)((decimal)Math.Clamp(fraction, 0, 1) * duration.Ticks));
        return player.SeekAsync(position, cancellationToken);
    }
}
