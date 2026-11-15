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
        var vm = new MainViewModel(player, new ScanMusicFolder(scanner), new PlaySong(player));

        await vm.LoadFolderAsync("cualquier-carpeta");

        Assert.Equal(2, vm.Songs.Count);
        Assert.Equal("2 canciones encontradas.", vm.Status);
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
            return new MainViewModel(player, new ScanMusicFolder(new FakeScanner()), new PlaySong(player));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FakeScanner(params Song[] songs) : IMusicFileScanner
    {
        public Task<IReadOnlyList<Song>> ScanAsync(string folder, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Song>>(songs);
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Complete(Exception? error = null) => PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
