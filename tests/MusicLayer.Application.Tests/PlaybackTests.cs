using System.IO;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;
using MusicLayer.Infrastructure.FileSystem;
using MusicLayer.Presentation.Wpf.ViewModels;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public async Task MissingFileNeverReachesPlayer()
    {
        var player = new FakePlayer();
        var useCase = new PlaySong(player);
        await Assert.ThrowsAsync<FileNotFoundException>(() => useCase.ExecuteAsync(new Song(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"))));
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(new Song(file), new CancellationToken(true)));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ScannerFindsNestedAudioAndIgnoresOtherFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MusicLayerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(folder, "album"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "song.MP3"), "");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
            File.WriteAllText(Path.Combine(folder, "album", "track.flac"), "");
            var scanner = new MusicFileScanner();
            var songs = await new ScanMusicFolder(scanner).ExecuteAsync(folder);
            Assert.Equal(2, songs.Count);
            Assert.Contains(songs, s => s.DisplayName == "track");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(folder, new CancellationToken(true)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task CompletionUpdatesViewModelAndDisposalUnsubscribes()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        var player = new FakePlayer();
        var vm = new MainViewModel(player, new ScanMusicFolder(new MusicFileScanner()), new PlaySong(player));
        SynchronizationContext.SetSynchronizationContext(previous);
        player.Complete();
        Assert.Equal("Reproducción finalizada.", vm.Status);
        player.Complete(new IOException("device failed"));
        Assert.Contains("device failed", vm.Status);
        await vm.DisposeAsync();
        var status = vm.Status;
        player.Complete();
        Assert.Equal(status, vm.Status);
        Assert.False(vm.IsReady);
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public string? PathPlayed { get; private set; }
        public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            PathPlayed = filePath;
            return Task.CompletedTask;
        }
        public void Complete(Exception? error = null) => PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
