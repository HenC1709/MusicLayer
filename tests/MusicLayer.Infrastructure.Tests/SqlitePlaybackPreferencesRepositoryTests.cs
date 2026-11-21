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
}
