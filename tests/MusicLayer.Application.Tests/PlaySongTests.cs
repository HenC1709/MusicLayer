using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class PlaySongTests
{
    [Fact]
    public async Task MissingFileNeverReachesPlayer()
    {
        var player = new FakePlayer();
        var useCase = new PlaySong(player);
        var missing = new Song(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => useCase.ExecuteAsync(missing));

        Assert.Null(player.PathPlayed);
    }

    [Fact]
    public async Task ExistingFileReachesPlayerAndCancellationIsRespected()
    {
        var file = Path.GetTempFileName();
        try
        {
            var player = new FakePlayer();
            var useCase = new PlaySong(player);

            await useCase.ExecuteAsync(new Song(file));
            Assert.Equal(file, player.PathPlayed);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => useCase.ExecuteAsync(new Song(file), new CancellationToken(true)));
        }
        finally { File.Delete(file); }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public string? PathPlayed { get; private set; }
       public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded { add { } remove { } }

        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            PathPlayed = filePath;
            return Task.CompletedTask;
        }

        public Task<bool> PauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
