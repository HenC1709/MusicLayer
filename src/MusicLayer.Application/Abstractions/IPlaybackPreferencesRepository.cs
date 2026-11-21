namespace MusicLayer.Application.Abstractions;

public interface IPlaybackPreferencesRepository
{
    Task<PlaybackPreferences> ReadAsync(CancellationToken cancellationToken = default);
    Task SaveFolderAsync(string folder, CancellationToken cancellationToken = default);
    Task SaveVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
}
