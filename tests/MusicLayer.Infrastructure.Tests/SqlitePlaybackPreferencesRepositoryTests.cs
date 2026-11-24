using MusicLayer.Application.Abstractions;
using MusicLayer.Infrastructure.Persistence;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class SqlitePlaybackPreferencesRepositoryTests : IDisposable
{
    private readonly DirectoryInfo sandbox = Directory.CreateTempSubdirectory("TonicPreferences-");
    private const string LibraryFolder = @"C:\Música; mezclas\L'été";

    private string DatabaseDirectory => Path.Combine(sandbox.FullName, "Preferencias; locales");
    private IPlaybackPreferencesRepository OpenRepository() => new SqlitePlaybackPreferencesRepository(DatabaseDirectory);

    [Fact]
    public async Task FirstReadCreatesTheDatabaseAndReturnsDefaults()
    {
        var repository = OpenRepository();
        Assert.False(Directory.Exists(DatabaseDirectory));

        Assert.Equal(new PlaybackPreferences(null, 100), await repository.ReadAsync());
        Assert.True(File.Exists(Path.Combine(DatabaseDirectory, "preferences.db")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task SavesVolumeBeforeFolderAndPreservesBothAcrossInstances(int volume)
    {
        var repository = OpenRepository();
        await repository.SaveVolumeAsync(volume);
        Assert.Equal(new PlaybackPreferences(null, volume), await OpenRepository().ReadAsync());

        await OpenRepository().SaveFolderAsync(LibraryFolder);
        Assert.Equal(new PlaybackPreferences(LibraryFolder, volume), await OpenRepository().ReadAsync());
    }

    [Fact]
    public async Task SavesFolderBeforeVolumeAndPreservesTheFolder()
    {
        var repository = OpenRepository();
        await repository.SaveFolderAsync(LibraryFolder);
        Assert.Equal(new PlaybackPreferences(LibraryFolder, 100), await repository.ReadAsync());

        await repository.SaveVolumeAsync(42);
        Assert.Equal(new PlaybackPreferences(LibraryFolder, 42), await OpenRepository().ReadAsync());
    }

    [Fact]
    public async Task ConcurrentFirstWritesPreserveBothSettings()
    {
        await Task.WhenAll(OpenRepository().SaveFolderAsync(LibraryFolder), OpenRepository().SaveVolumeAsync(27));

        Assert.Equal(new PlaybackPreferences(LibraryFolder, 27), await OpenRepository().ReadAsync());
    }

    [Fact]
    public async Task ReportsUnavailableDatabaseAndCanRetryAfterAccessIsRestored()
    {
        var blockedPath = Path.Combine(DatabaseDirectory, "preferences.db");
        Directory.CreateDirectory(blockedPath);
        var repository = OpenRepository();

        await Assert.ThrowsAsync<IOException>(() => repository.ReadAsync());
        await Assert.ThrowsAsync<IOException>(() => repository.SaveVolumeAsync(42));

        Directory.Delete(blockedPath);
        await repository.SaveVolumeAsync(42);
        Assert.Equal(new PlaybackPreferences(null, 42), await repository.ReadAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task RejectsVolumeOutsideSupportedRange(int volume)
    {
        var repository = OpenRepository();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await repository.SaveVolumeAsync(volume));
        Assert.False(Directory.Exists(DatabaseDirectory));
    }

    [Fact]
    public async Task CancelledWriteLeavesExistingPreferencesIntact()
    {
        var repository = OpenRepository();
        await repository.SaveVolumeAsync(63);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.SaveVolumeAsync(0, new CancellationToken(true)));
        Assert.Equal(new PlaybackPreferences(null, 63), await repository.ReadAsync());
    }

    public void Dispose() => sandbox.Delete(recursive: true);

    [Fact]
    public async Task FavoritesSurviveReopeningAndCanBeRemovedAcrossPathCasing()
    {
        const string first = @"C:\Música\Primera.flac";
        const string second = @"C:\Otra\Segunda.wav";
        await OpenRepository().SaveVolumeAsync(35);
        Assert.Empty(await OpenRepository().ReadFavoritesAsync());
        await OpenRepository().SaveFavoriteAsync(first, true);
        await OpenRepository().SaveFavoriteAsync(first.ToUpperInvariant(), true);
        await OpenRepository().SaveFavoriteAsync(second, true);
        Assert.Equal(2, (await OpenRepository().ReadFavoritesAsync()).Count);

        await OpenRepository().SaveFavoriteAsync(first.ToUpperInvariant(), false);
        Assert.Equal(second, Assert.Single(await OpenRepository().ReadFavoritesAsync()));
        Assert.Equal(35, (await OpenRepository().ReadAsync()).VolumePercent);
    }

    [Fact]
    public async Task ExistingDatabaseGainsOptionsAndClearsBookmarkWhenDisabled()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DatabaseDirectory, "preferences.db"), Pooling = false
        }.ToString();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE playback_preferences (id INTEGER PRIMARY KEY, library_folder TEXT, volume_percent INTEGER);
                INSERT INTO playback_preferences VALUES (1, $folder, 35);
                """;
            command.Parameters.AddWithValue("$folder", LibraryFolder);
            command.ExecuteNonQuery();
        }

        var original = new PlaybackPreferences(LibraryFolder, 35);
        Assert.Equal(original, await OpenRepository().ReadAsync());
        var options = new PlayerOptions(true, false, false, false);
        var bookmark = new PlaybackBookmark(Path.Combine(LibraryFolder, "Pista.flac"), TimeSpan.FromSeconds(12));
        await OpenRepository().SaveOptionsAsync(options);
        await OpenRepository().SaveBookmarkAsync(bookmark);
        Assert.Equal(original with { Options = options, Bookmark = bookmark }, await OpenRepository().ReadAsync());
        await OpenRepository().SaveOptionsAsync(options with { RememberPosition = false });
        await OpenRepository().SaveOptionsAsync(options);
        Assert.Equal(original with { Options = options }, await OpenRepository().ReadAsync());
    }
}
