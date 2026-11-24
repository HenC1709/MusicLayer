namespace MusicLayer.Application.Abstractions;

public interface IPlaybackPreferencesRepository
{
    Task<PlaybackPreferences> ReadAsync(CancellationToken cancellationToken = default);
    Task SaveFolderAsync(string folder, CancellationToken cancellationToken = default);
    Task SaveVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
    Task SaveOptionsAsync(PlayerOptions options, CancellationToken cancellationToken = default);
    Task SaveBookmarkAsync(PlaybackBookmark? bookmark, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ReadFavoritesAsync(CancellationToken cancellationToken = default);
    Task SaveFavoriteAsync(string path, bool isFavorite, CancellationToken cancellationToken = default);
}
