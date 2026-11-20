using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class PlaybackSessionTests
{
    [Fact]
    public async Task PlaysSelectionAndNeighborsWithoutWrapping()
    {
        using var tracks = new TestTracks();
        var player = new FakePlayer();
        var session = new PlaybackSession(new PlaySong(player));

        Assert.Null(await session.PlayNextAsync());
        await session.ReplaceQueueAsync([tracks.First, tracks.Second]);
        Assert.Null(await session.PlayPreviousAsync());
        Assert.Same(tracks.Second, await session.PlayAtAsync(1));
        Assert.Null(await session.PlayNextAsync());
        Assert.Same(tracks.First, await session.PlayPreviousAsync());
        Assert.Null(await session.PlayPreviousAsync());
        Assert.Same(tracks.Second, await session.PlayNextAsync());
        Assert.Equal(new[] { tracks.Second.FilePath, tracks.First.FilePath, tracks.Second.FilePath }, player.Requests);
    }

    [Fact]
    public async Task FailedOrCancelledStartLeavesTheSameTrackAvailableToRetry()
    {
        using var tracks = new TestTracks();
        var player = new FakePlayer();
        var session = new PlaybackSession(new PlaySong(player));
        await session.ReplaceQueueAsync([tracks.First, tracks.Second]);
        await session.PlayNextAsync();

        player.Start = _ => throw new IOException("Audio output failed.");
        await Assert.ThrowsAsync<IOException>(() => session.PlayNextAsync());
        using var cancellation = new CancellationTokenSource();
        player.Start = token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PlayNextAsync(cancellation.Token));
        player.Start = _ => Task.CompletedTask;

        Assert.Same(tracks.Second, await session.PlayNextAsync());
        Assert.Null(await session.PlayNextAsync());
    }

    [Fact]
    public async Task ReplacingQueueClearsSelectionWithoutStartingAudio()
    {
        using var tracks = new TestTracks();
        var player = new FakePlayer();
        var session = new PlaybackSession(new PlaySong(player));
        await session.ReplaceQueueAsync([tracks.First, tracks.Second]);
        await session.PlayAtAsync(1);

        await session.ReplaceQueueAsync([tracks.Second, tracks.First]);
        Assert.Single(player.Requests);
        Assert.Null(await session.PlayPreviousAsync());
        Assert.Same(tracks.Second, await session.PlayNextAsync());
        await session.ReplaceQueueAsync([]);
        Assert.Null(await session.PlayNextAsync());
        Assert.Equal(2, player.Requests.Count);
    }

    [Fact]
    public async Task OverlappingNextRequestsStartDifferentTracksInOrder()
    {
        using var tracks = new TestTracks();
        var firstStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new FakePlayer { Start = _ => firstStart.Task };
        var session = new PlaybackSession(new PlaySong(player));
        await session.ReplaceQueueAsync([tracks.First, tracks.Second]);

        var first = session.PlayNextAsync();
        var second = session.PlayNextAsync();
        var secondWaited = !second.IsCompleted;
        firstStart.SetResult();

        Assert.Same(tracks.First, await first);
        Assert.Same(tracks.Second, await second);
        Assert.True(secondWaited);
        Assert.Equal(new[] { tracks.First.FilePath, tracks.Second.FilePath }, player.Requests);
    }

    private sealed class TestTracks : IDisposable
    {
        public Song First { get; } = new(Path.GetTempFileName());
        public Song Second { get; } = new(Path.GetTempFileName());

        public void Dispose()
        {
            File.Delete(First.FilePath);
            File.Delete(Second.FilePath);
        }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public List<string> Requests { get; } = [];
        public Func<CancellationToken, Task> Start { get; set; } = _ => Task.CompletedTask;
        public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded { add { } remove { } }

        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Requests.Add(filePath);
            return Start(cancellationToken);
        }

        public Task<bool> PauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PlaybackProgress?> GetProgressAsync(CancellationToken cancellationToken = default) => Task.FromResult<PlaybackProgress?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
