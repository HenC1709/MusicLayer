using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class PlaySong(IAudioPlayer player)
{
    public Task ExecuteAsync(Song song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(song.FilePath))
            throw new FileNotFoundException("El archivo de música ya no existe.", song.FilePath);
        return player.PlayAsync(song.FilePath, cancellationToken);
    }
}
