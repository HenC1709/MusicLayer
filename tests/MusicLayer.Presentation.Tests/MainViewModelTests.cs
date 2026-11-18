using System.IO;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;
using MusicLayer.Presentation.Wpf.ViewModels;
using Xunit;

namespace MusicLayer.Presentation.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task LoadFolderShowsSongsAndCount()
    {
        var player = new FakePlayer();
        var scanner = new FakeScanner(new Song("a.mp3"), new Song("b.wav"));
        await using var vm = CreateViewModel(player, scanner);

        await vm.LoadFolderAsync("cualquier-carpeta");

        Assert.Equal(2, vm.Songs.Count);
        Assert.Equal("2 canciones encontradas.", vm.Status);
    }

    [Fact]
    public async Task NavigationUpdatesSelectionByIndexEvenWithDuplicateSongs()
    {
        var path = Path.GetTempFileName();
        try
        {
            var song = new Song(path);
            var player = new FakePlayer();
            var scanner = new FakeScanner(song, song);
            await using var vm = CreateViewModel(player, scanner);

            await vm.LoadFolderAsync("cualquier-carpeta");
            Assert.Equal(-1, vm.SelectedIndex);
            await vm.PlayNextAsync();
            Assert.Equal(0, vm.SelectedIndex);
            await vm.PlayNextAsync();
            Assert.Equal(1, vm.SelectedIndex);
            await vm.PlayNextAsync();
            Assert.Equal(1, vm.SelectedIndex);
            Assert.Equal("No hay una canción siguiente.", vm.Status);
            await vm.PlayPreviousAsync();
            Assert.Equal(0, vm.SelectedIndex);
            Assert.Equal(3, player.PlayCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PauseKeepsQueuePositionAndIsOnlyAvailableWhilePlaying()
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        try
        {
            var player = new FakePlayer();
            await using var vm = CreateViewModel(player, new FakeScanner(new Song(first), new Song(second)));
            await vm.LoadFolderAsync("cualquier-carpeta");
            Assert.False(vm.CanPause);
            await vm.PauseAsync();
            Assert.Equal(0, player.PauseCount);

            await vm.PlayNextAsync();
            Assert.True(vm.CanPause);
            await vm.PauseAsync();
            Assert.False(vm.CanPause);
            Assert.Equal("Reproducción pausada.", vm.Status);
            Assert.Equal(0, vm.SelectedIndex);
            await vm.PauseAsync();
            Assert.Equal(1, player.PauseCount);
            Assert.Equal(1, player.PlayCount);

            await vm.PlayNextAsync();
            Assert.Equal(1, vm.SelectedIndex);
            Assert.True(vm.CanPause);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task ResumeRestartsPausedOutputWithoutPlayingTheFileAgain()
    {
        var path = Path.GetTempFileName();
        try
        {
            var player = new FakePlayer();
            await using var vm = CreateViewModel(player, new FakeScanner(new Song(path)));
            await vm.LoadFolderAsync("cualquier-carpeta");
            await vm.ResumeAsync();
            Assert.Equal(0, player.ResumeCount);

            await vm.PlayNextAsync();
            Assert.False(vm.CanResume);
            await vm.PauseAsync();
            Assert.True(vm.CanResume);

            await vm.ResumeAsync();
            Assert.Equal("Reproducción reanudada.", vm.Status);
            Assert.Equal(0, vm.SelectedIndex);
            Assert.False(vm.CanResume);
            Assert.True(vm.CanPause);
            await vm.ResumeAsync();
            Assert.Equal(1, player.ResumeCount);
            Assert.Equal(1, player.PlayCount);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopClearsAudioControlsAndKeepsNavigationPosition(bool pauseFirst)
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        try
        {
            var player = new FakePlayer();
            await using var vm = CreateViewModel(player, new FakeScanner(new Song(first), new Song(second)));
            await vm.LoadFolderAsync("cualquier-carpeta");
            Assert.False(vm.CanStop);
            await vm.PlayNextAsync();
            if (pauseFirst) await vm.PauseAsync();

            Assert.True(vm.CanStop);
            await vm.StopAsync();

            Assert.Equal("Reproducción detenida.", vm.Status);
            Assert.Equal(0, vm.SelectedIndex);
            Assert.False(vm.CanStop);
            Assert.False(vm.CanPause);
            Assert.False(vm.CanResume);
            await vm.StopAsync();
            Assert.Equal(1, player.StopCount);
            Assert.Equal(1, player.PlayCount);

            await vm.PlayNextAsync();
            Assert.Equal(1, vm.SelectedIndex);
            await vm.StopAsync();
            await vm.PlayPreviousAsync();
            Assert.Equal(0, vm.SelectedIndex);
            Assert.Equal(3, player.PlayCount);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task CompletionUpdatesViewModelAndDisposalUnsubscribes()
    {
        var player = new FakePlayer();
        var vm = CreateOnImmediateContext(player);

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

    // El ViewModel captura el SynchronizationContext al crearse; aquí se ejecuta en línea.
    private static MainViewModel CreateOnImmediateContext(FakePlayer player)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            return CreateViewModel(player, new FakeScanner());
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private static MainViewModel CreateViewModel(FakePlayer player, FakeScanner scanner)
        => new(player, new ScanMusicFolder(scanner), new PlaybackSession(new PlaySong(player)),
            new PausePlayback(player), new ResumePlayback(player), new StopPlayback(player));

    private sealed class FakeScanner(params Song[] songs) : IMusicFileScanner
    {
        public Task<IReadOnlyList<Song>> ScanAsync(string folder, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Song>>(songs);
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }
        public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            PlayCount++;
            return Task.CompletedTask;
        }
        public void Complete(Exception? error = null) => PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error));
        public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCount++;
            return Task.FromResult(true);
        }

        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCount++;
            return Task.FromResult(true);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
