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
        Assert.Equal(NoticeKind.Imported, vm.Notice);
    }

    [Fact]
    public async Task EmptyImportIsDistinctFromFirstLaunchAndScanInProgress()
    {
        var result = new TaskCompletionSource<IReadOnlyList<Song>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner { Scan = _ => result.Task };
        await using var vm = CreateViewModel(new FakePlayer(), scanner);
        Assert.False(vm.HasImportedFolder);

        var scan = vm.LoadFolderAsync("vacía");
        Assert.Equal(NoticeKind.Importing, vm.Notice);
        Assert.False(vm.IsReady);
        result.SetResult([]);
        await scan;

        Assert.True(vm.HasImportedFolder);
        Assert.True(vm.IsReady);
        Assert.Empty(vm.Songs);
        Assert.Equal("La carpeta no contiene audio compatible.", vm.Status);
        Assert.False(vm.CanTogglePlayback);
    }

    [Fact]
    public async Task FailedImportKeepsLibraryAndSelectionUntilAnotherImportSucceeds()
    {
        var song = new Song("primera.wav");
        var scanner = new FakeScanner(song);
        await using var vm = CreateViewModel(new FakePlayer(), scanner);
        await vm.LoadFolderAsync("primera");
        vm.SelectedIndex = 0;

        scanner.Scan = _ => throw new UnauthorizedAccessException("acceso denegado");
        await vm.LoadFolderAsync("protegida");
        Assert.Equal(NoticeKind.FolderError, vm.Notice);
        Assert.Same(song, Assert.Single(vm.Songs));
        Assert.Equal(0, vm.SelectedIndex);

        scanner.Scan = _ => Task.FromResult<IReadOnlyList<Song>>([new Song("nueva.wav")]);
        await vm.LoadFolderAsync("otra");
        Assert.Equal(NoticeKind.Imported, vm.Notice);
        Assert.Equal("1 canción encontrada.", vm.Status);
        Assert.Null(vm.StatusDetail);
        Assert.Equal(-1, vm.SelectedIndex);
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
    public async Task SelectionAndFolderChangesDoNotReplaceTheActiveTrack()
    {
        var path = Path.GetTempFileName();
        try
        {
            var song = new Song(path);
            var other = new Song(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"));
            var player = new FakePlayer();
            await using var vm = CreateOnImmediateContext(player, new FakeScanner(song, song, other));
            await vm.LoadFolderAsync("música");

            Assert.False(vm.CanPrevious);
            Assert.True(vm.CanTogglePlayback);
            await vm.TogglePlaybackAsync();
            await vm.PlayNextAsync();
            vm.SelectedIndex = 2;
            Assert.Equal(1, vm.CurrentSongIndex);
            await vm.PlaySelectedAsync(); // The missing file fails before the active output is touched.
            Assert.Equal(NoticeKind.PlaybackError, vm.Notice);
            Assert.Contains(Path.GetFileName(other.FilePath), vm.Status);
            Assert.Same(song, vm.CurrentSong);
            Assert.Equal(1, vm.CurrentSongIndex);

            await vm.TogglePlaybackAsync();
            Assert.Equal("En pausa", vm.PlaybackLabel);
            await vm.LoadFolderAsync("otra carpeta");
            Assert.Same(song, vm.CurrentSong);
            Assert.Equal(-1, vm.CurrentSongIndex);
            await vm.TogglePlaybackAsync();
            Assert.Equal(2, player.PlayCount);
            Assert.Equal(1, player.ResumeCount);
            player.Complete(new IOException("salida desconectada"));
            Assert.Null(vm.CurrentSong);
            Assert.Equal("Sin reproducción", vm.PlaybackLabel);
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
            Assert.Null(vm.CurrentSong);
            Assert.Equal(-1, vm.CurrentSongIndex);
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
    public async Task VolumeCanBeSetBeforeAndDuringPlaybackAndIsKeptByTransportControls()
    {
        var path = Path.GetTempFileName();
        try
        {
            var player = new FakePlayer();
            var track = new Song(path);
            await using var vm = CreateViewModel(player, new FakeScanner(track, track));
            await vm.LoadFolderAsync("cualquier-carpeta");

            Assert.Equal(100, vm.VolumePercent);
            vm.VolumePercent = 25;
            await vm.PlayNextAsync();
            Assert.Equal(new[] { 25 }, player.VolumesAtStart);

            vm.VolumePercent = 0;
            Assert.Equal(0, player.VolumePercent);
            Assert.Equal(1, player.PlayCount);

            await vm.PauseAsync();
            await vm.ResumeAsync();
            Assert.Equal(0, player.VolumePercent);
            await vm.StopAsync();
            await vm.PlayNextAsync();

            Assert.Equal(0, vm.VolumePercent);
            Assert.Equal(new[] { 25, 0 }, player.VolumeRequests);
            Assert.Equal(new[] { 25, 0 }, player.VolumesAtStart);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PlaybackWaitsForTheLatestVolumeAfterRapidChanges()
    {
        var path = Path.GetTempFileName();
        try
        {
            var firstAdjustment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var player = new FakePlayer
            {
                ApplyVolume = percent => percent == 20 ? firstAdjustment.Task : Task.CompletedTask
            };
            await using var vm = CreateViewModel(player, new FakeScanner(new Song(path)));
            await vm.LoadFolderAsync("cualquier-carpeta");

            vm.VolumePercent = 20;
            vm.VolumePercent = 40;
            vm.VolumePercent = 65;
            var playback = vm.PlayNextAsync();
            var playCountBeforeAdjustment = player.PlayCount;
            firstAdjustment.SetResult();
            await playback;

            Assert.Equal(0, playCountBeforeAdjustment);
            Assert.Equal(new[] { 20, 65 }, player.VolumeRequests);
            Assert.Equal(new[] { 65 }, player.VolumesAtStart);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VolumeFailureRestoresLastAppliedValue()
    {
        var player = new FakePlayer
        {
            ApplyVolume = percent => percent == 40
                ? Task.FromException(new IOException("audio device unavailable"))
                : Task.CompletedTask
        };
        await using var vm = CreateViewModel(player, new FakeScanner());

        vm.VolumePercent = 75;
        vm.VolumePercent = 40;

        Assert.Equal(75, vm.VolumePercent);
        Assert.Equal(75, player.VolumePercent);
        Assert.Contains("audio device unavailable", vm.Status);
    }

    [Fact]
    public async Task ProgressAdvancesWithOutputAndFreezesWhilePaused()
    {
        var path = Path.GetTempFileName();
        try
        {
            var duration = TimeSpan.FromMinutes(2);
            var player = new FakePlayer { Progress = new(TimeSpan.Zero, duration) };
            await using var vm = CreateViewModel(player, new FakeScanner(new Song(path)));
            await vm.LoadFolderAsync("cualquier-carpeta");
            await vm.PlayNextAsync();
            Assert.Equal("02:00", vm.DurationText);

            player.Progress = new(TimeSpan.FromSeconds(29), duration);
            await vm.RefreshPlaybackProgressAsync();
            Assert.Equal("00:29", vm.ElapsedTimeText);

            player.Progress = new(TimeSpan.FromSeconds(30), duration);
            await vm.PauseAsync();
            Assert.Equal("00:30", vm.ElapsedTimeText);
            Assert.Equal(0.25, vm.ProgressFraction);

            player.Progress = new(TimeSpan.FromSeconds(60), duration);
            await vm.RefreshPlaybackProgressAsync();
            Assert.Equal("00:30", vm.ElapsedTimeText);

            await vm.ResumeAsync();
            await vm.RefreshPlaybackProgressAsync();
            Assert.Equal("01:00", vm.ElapsedTimeText);
            Assert.Equal(0.5, vm.ProgressFraction);

            await vm.StopAsync();
            Assert.Equal("00:00", vm.ElapsedTimeText);
            Assert.Equal("--:--", vm.DurationText);
            Assert.Equal(0, vm.ProgressFraction);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ChangingTrackDiscardsThePreviousTrackReadingInFlight()
    {
        var path = Path.GetTempFileName();
        try
        {
            var previousDuration = TimeSpan.FromMinutes(2);
            var player = new FakePlayer { Progress = new(TimeSpan.Zero, previousDuration) };
            var track = new Song(path);
            await using var vm = CreateViewModel(player, new FakeScanner(track, track));
            await vm.LoadFolderAsync("cualquier-carpeta");
            await vm.PlayNextAsync();

            var oldReading = new TaskCompletionSource<PlaybackProgress?>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.ReadProgress = _ => oldReading.Task;
            var refresh = vm.RefreshPlaybackProgressAsync();
            try
            {
                player.ReadProgress = null;
                player.Progress = new(TimeSpan.Zero, TimeSpan.FromMinutes(3));
                await vm.PlayNextAsync();
            }
            finally
            {
                oldReading.SetResult(new PlaybackProgress(TimeSpan.FromMinutes(1), previousDuration));
                await refresh;
            }

            Assert.Equal(1, vm.SelectedIndex);
            Assert.Equal("03:00", vm.DurationText);
            Assert.Equal("00:00", vm.ElapsedTimeText);
            Assert.Equal(0, vm.ProgressFraction);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NaturalCompletionResetsProgressAndIgnoresAnOutstandingReading()
    {
        var path = Path.GetTempFileName();
        try
        {
            var duration = TimeSpan.FromMinutes(1);
            var player = new FakePlayer { Progress = new(TimeSpan.FromSeconds(50), duration) };
            await using var vm = CreateOnImmediateContext(player, new FakeScanner(new Song(path)));
            await vm.LoadFolderAsync("cualquier-carpeta");
            await vm.PlayNextAsync();
            Assert.Equal("00:50", vm.ElapsedTimeText);

            var finalReading = new TaskCompletionSource<PlaybackProgress?>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.ReadProgress = _ => finalReading.Task;
            var refresh = vm.RefreshPlaybackProgressAsync();
            try { player.Complete(); }
            finally
            {
                finalReading.SetResult(new PlaybackProgress(duration, duration));
                await refresh;
            }

            Assert.Equal("Reproducción finalizada.", vm.Status);
            Assert.Null(vm.CurrentSong);
            Assert.Equal(-1, vm.CurrentSongIndex);
            Assert.Equal("00:00", vm.ElapsedTimeText);
            Assert.Equal("--:--", vm.DurationText);
            Assert.Equal(0, vm.ProgressFraction);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CompletionUpdatesViewModelAndDisposalUnsubscribes()
    {
        var player = new FakePlayer();
        var vm = CreateOnImmediateContext(player);

        player.Complete();
        Assert.Equal("Reproducción finalizada.", vm.Status);

        player.Complete(new IOException("device failed"));
        Assert.Equal(NoticeKind.PlaybackError, vm.Notice);
        Assert.Equal("device failed", vm.StatusDetail);

        await vm.DisposeAsync();
        var status = vm.Status;
        player.Complete();

        Assert.Equal(status, vm.Status);
        Assert.False(vm.IsReady);
    }

    // El ViewModel captura el SynchronizationContext al crearse; aquí se ejecuta en línea.
    private static MainViewModel CreateOnImmediateContext(FakePlayer player, FakeScanner? scanner = null)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        try
        {
            return CreateViewModel(player, scanner ?? new FakeScanner());
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    [Fact]
    public async Task CoverFollowsSelectionBeforePlayAndStaysWithTheActiveSong()
    {
        var path = Path.GetTempFileName();
        try
        {
            var song = new Song(path);
            var other = new Song("otra.flac");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAQSURBVBhXY/jPwPCfARkAAB7zAf+x9MCaAAAAAElFTkSuQmCC");
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var vm = CreateViewModel(new FakePlayer(), new FakeScanner(song, other),
                new FakeCoverReader(file => file == path ? png : null));
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.CoverImage) && vm.CoverImage is not null) loaded.TrySetResult();
            };
            await vm.LoadFolderAsync("musica");
            vm.SelectedIndex = 0;
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(vm.CurrentSong);
            Assert.True(vm.CoverImage!.IsFrozen);
            var preview = vm.CoverImage;
            await vm.PlaySelectedAsync();
            vm.SelectedIndex = 1;
            await vm.PauseAsync();
            Assert.Same(song, vm.CoverSong);
            Assert.Same(preview, vm.CoverImage);
            await vm.StopAsync();
            Assert.Same(other, vm.CoverSong);
            Assert.Null(vm.CoverImage);
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeCoverReader(Func<string, byte[]?> read) : ICoverArtReader
    {
        public byte[]? ReadEmbeddedCover(string filePath) => read(filePath);
    }

    private static MainViewModel CreateViewModel(FakePlayer player, FakeScanner scanner, ICoverArtReader? covers = null)
        => new(player, new ScanMusicFolder(scanner), new PlaybackSession(new PlaySong(player)),
            new PausePlayback(player), new ResumePlayback(player), new StopPlayback(player), new SetPlaybackVolume(player),
            covers ?? new FakeCoverReader(_ => null));

    private sealed class FakeScanner(params Song[] songs) : IMusicFileScanner
    {
        public Func<string, Task<IReadOnlyList<Song>>> Scan { get; set; }
            = _ => Task.FromResult<IReadOnlyList<Song>>(songs);

        public Task<IReadOnlyList<Song>> ScanAsync(string folder, CancellationToken cancellationToken = default)
            => Scan(folder);
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }
        public int VolumePercent { get; private set; } = IAudioPlayer.DefaultVolumePercent;
        public List<int> VolumeRequests { get; } = [];
        public List<int> VolumesAtStart { get; } = [];
        public Func<int, Task> ApplyVolume { get; init; } = _ => Task.CompletedTask;
        public PlaybackProgress? Progress { get; set; }
        public Func<CancellationToken, Task<PlaybackProgress?>>? ReadProgress { get; set; }
        public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            PlayCount++;
            VolumesAtStart.Add(VolumePercent);
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

        public async Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default)
        {
            VolumeRequests.Add(percent);
            await ApplyVolume(percent);
            VolumePercent = percent;
        }

        public Task<PlaybackProgress?> GetProgressAsync(CancellationToken cancellationToken = default)
            => ReadProgress?.Invoke(cancellationToken) ?? Task.FromResult(Progress);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
