using MusicLayer.Domain.Entities;
using MusicLayer.Infrastructure.Persistence;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class SqlitePlaylistRepositoryTests
{
    [Fact]
    public async Task ReopeningPreservesNamesAndOrderAlongsideExistingPreferences()
    {
        var storage = Directory.CreateTempSubdirectory();
        try
        {
            var preferences = new SqlitePlaybackPreferencesRepository(storage.FullName);
            await preferences.SaveVolumeAsync(35);
            var repository = new SqlitePlaylistRepository(storage.FullName);
            var playlist = new Playlist(Guid.NewGuid(), "Viajes", ["b.flac", "a.flac", "c.flac"])
            {
                Genre = "Rock",
                Description = "Para viajes largos.",
                Cover = [1, 2, 3]
            };
            await repository.SaveAsync(playlist);
            await repository.SaveAsync(new Playlist(Guid.NewGuid(), "Vacía", []));
            await repository.SaveAsync(playlist with { Name = "Ruta", TrackPaths = ["c.flac", "b.flac"] });

            var reopened = await new SqlitePlaylistRepository(storage.FullName).ReadAsync();
            var edited = Assert.Single(reopened, entry => entry.Id == playlist.Id);
            Assert.Equal("Ruta", edited.Name);
            Assert.Equal(new[] { "c.flac", "b.flac" }, edited.TrackPaths);
            Assert.Equal(playlist.Genre, edited.Genre);
            Assert.Equal(playlist.Description, edited.Description);
            Assert.Equal(playlist.Cover, edited.Cover);
            var empty = Assert.Single(reopened, entry => entry.Name == "Vacía");
            Assert.Empty(empty.TrackPaths);
            Assert.Null(empty.Cover);
            Assert.Equal(35, (await preferences.ReadAsync()).VolumePercent);
        }
        finally { storage.Delete(recursive: true); }
    }
}
