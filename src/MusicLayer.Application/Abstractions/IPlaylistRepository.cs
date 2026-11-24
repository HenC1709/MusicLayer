using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.Abstractions;

public interface IPlaylistRepository
{
    Task<IReadOnlyList<Playlist>> ReadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Playlist playlist, CancellationToken cancellationToken = default);
}
