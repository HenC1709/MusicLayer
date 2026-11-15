using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class PlaySong(IAudioPlayer player)
{
    public Task ExecuteAsync(Song song, CancellationToken cancellationToken = default)
        => player.PlayAsync(song.FilePath, cancellationToken);
}
