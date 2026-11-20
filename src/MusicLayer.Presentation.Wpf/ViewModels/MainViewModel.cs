using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public enum NoticeKind { Information, Importing, Imported, FolderError, PlaybackError, Error }

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAudioPlayer player;
    private readonly ScanMusicFolder scanner;
    private readonly PlaybackSession session;
    private readonly PausePlayback pause;
    private readonly ResumePlayback resume;
    private readonly StopPlayback stop;
    private readonly SetPlaybackVolume setVolume;
    private readonly ICoverArtReader coverReader;
    private readonly SynchronizationContext context;
    private readonly CancellationTokenSource lifetime = new();
    private Task pending = Task.CompletedTask;
    private Task pendingVolume = Task.CompletedTask;
    private Task pendingProgress = Task.CompletedTask;
    private Task pendingCover = Task.CompletedTask;
    private string? coverPath;
    private bool closing;
    private bool busy;
    private bool changingPlayback;
    private AudioState audioState;
    private int completionVersion;
    private int progressVersion;
    private PlaybackProgress? progress;
    private int selectedIndex = -1;
    private int volumePercent = IAudioPlayer.DefaultVolumePercent;
    private int appliedVolumePercent = IAudioPlayer.DefaultVolumePercent;
    private IReadOnlyList<Song> songs = [];

    public MainViewModel(IAudioPlayer player, ScanMusicFolder scanner, PlaybackSession session,
        PausePlayback pause, ResumePlayback resume, StopPlayback stop, SetPlaybackVolume setVolume, ICoverArtReader coverReader,
        ILyricsProvider lyricsProvider)
    {
        this.player = player;
        this.scanner = scanner;
        this.session = session;
        this.pause = pause;
        this.resume = resume;
        this.stop = stop;
        this.setVolume = setVolume;
        this.coverReader = coverReader;
        Lyrics = new LyricsViewModel(lyricsProvider);
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        player.PlaybackEnded += OnPlaybackEnded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public LyricsViewModel Lyrics { get; }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value) return;
            selectedIndex = value;
            Notify();
            RefreshCover();
            Notify(nameof(CanPlaySelected));
            Notify(nameof(CanTogglePlayback));
        }
    }
    public IReadOnlyList<Song> Songs { get => songs; private set { songs = value; Notify(); } }
    public string Status { get; private set; } = "Elige una carpeta con música.";
    public string? StatusDetail { get; private set; }
    public NoticeKind Notice { get; private set; }
    public bool HasImportedFolder { get; private set; }
    public bool IsReady => !busy && !closing;
    public Song? CurrentSong
    {
        get;
        private set { field = value; Lyrics.ChangeTrack(value); RefreshCover(); }
    }
    public Song? CoverSong => CurrentSong ?? (SelectedIndex >= 0 && SelectedIndex < Songs.Count ? Songs[SelectedIndex] : null);
    public string CoverLabel => CurrentSong is null && CoverSong is not null ? "Selección" : PlaybackLabel;
    public string? CoverDescription => CoverSong?.Metadata switch
    {
        { Artist: { } artist, Album: { } album } => $"{artist} · {album}",
        { Artist: { } artist } => artist,
        { Album: { } album } => album,
        _ => CoverSong?.FolderName
    };
    public BitmapSource? CoverImage { get; private set; }
    public int CurrentSongIndex => CurrentSong is null ? -1 : session.CurrentIndex ?? -1;
    public bool CanPlaySelected => IsReady && SelectedIndex >= 0 && SelectedIndex < Songs.Count;
    public bool CanTogglePlayback => CanStop || CanPlaySelected || CanNext;
    public bool CanNext => IsReady && session.HasNext;
    public bool CanPrevious => IsReady && session.HasPrevious;
    public bool IsPlaying => audioState == AudioState.Playing;
    public string PlaybackLabel => audioState switch
    {
        AudioState.Playing => "En reproducción",
        AudioState.Paused => "En pausa",
        _ => "Sin reproducción"
    };
    public string TransportLabel => audioState switch
    {
        AudioState.Playing => "Pausar",
        AudioState.Paused => "Reanudar",
        _ => "Reproducir"
    };
    public bool CanPause => IsReady && audioState == AudioState.Playing;
    public bool CanResume => IsReady && audioState == AudioState.Paused;
    public bool CanStop => IsReady && audioState != AudioState.Inactive;
    public double ProgressFraction => progress is { } value
        ? value.Elapsed.TotalSeconds / value.Duration.TotalSeconds
        : 0;
    public string ElapsedTimeText => FormatPlaybackTime(progress?.Elapsed ?? TimeSpan.Zero);
    public string DurationText => progress is { } value ? FormatPlaybackTime(value.Duration) : "--:--";

    public int VolumePercent
    {
        get => volumePercent;
        set
        {
            if (closing || volumePercent == value) return;
            volumePercent = value;
            Notify();
            if (pendingVolume.IsCompleted)
                pendingVolume = ApplyVolumeChangesAsync();
        }
    }

    public Task LoadFolderAsync(string folder) => RunAsync(async () =>
    {
        ShowStatus("Buscando archivos MP3, WAV y FLAC…", NoticeKind.Importing);
        IReadOnlyList<Song> scannedSongs;
        try
        {
            scannedSongs = await scanner.ExecuteAsync(folder, lifetime.Token);
            await session.ReplaceQueueAsync(scannedSongs, lifetime.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ShowStatus("No se pudo leer la carpeta. Comprueba el acceso o elige otra.", NoticeKind.FolderError, error.Message);
            return;
        }
        HasImportedFolder = true;
        Notify(nameof(HasImportedFolder));
        Songs = scannedSongs;
        SelectedIndex = -1;
        if (Songs.Count == 0)
            ShowStatus("La carpeta no contiene audio compatible.");
        else
            ShowStatus(Songs.Count == 1 ? "1 canción encontrada." : $"{Songs.Count} canciones encontradas.", NoticeKind.Imported);
    });

    public Task PlaySelectedAsync() => RunTransportAsync(async () =>
    {
        var index = SelectedIndex;
        if (index < 0 || index >= Songs.Count) { ShowStatus("Selecciona una canción."); return; }
        await PlayAsync(token => session.PlayAtAsync(index, token), "Selecciona una canción.");
    });

    public Task PlayNextAsync() => RunTransportAsync(() =>
        PlayAsync(session.PlayNextAsync, "No hay una canción siguiente."));

    public Task TogglePlaybackAsync() => audioState switch
    {
        AudioState.Playing => PauseAsync(),
        AudioState.Paused => ResumeAsync(),
        _ => CanPlaySelected ? PlaySelectedAsync() : PlayNextAsync()
    };

    public Task PlayPreviousAsync() => RunTransportAsync(() =>
        PlayAsync(session.PlayPreviousAsync, "No hay una canción anterior."));

    public Task PauseAsync()
    {
        if (!CanPause) return Task.CompletedTask;

        return RunTransportAsync(async () =>
        {
            var version = completionVersion;
            var paused = await pause.ExecuteAsync(lifetime.Token);
            if (version != completionVersion) return;

            audioState = paused ? AudioState.Paused : AudioState.Inactive;
            if (!paused) CurrentSong = null;
            ShowStatus(paused ? "Reproducción pausada." : "No hay audio en reproducción.");
            if (paused)
                await ReadProgressAsync();
            else
                SetProgress(null);
        });
    }

    public Task ResumeAsync()
    {
        if (!CanResume) return Task.CompletedTask;

        return RunTransportAsync(async () =>
        {
            var version = completionVersion;
            var resumed = await resume.ExecuteAsync(lifetime.Token);
            if (version != completionVersion) return;

            audioState = resumed ? AudioState.Playing : AudioState.Inactive;
            if (!resumed) CurrentSong = null;
            ShowStatus(resumed ? "Reproducción reanudada." : "No hay audio en pausa.");
            if (!resumed)
                SetProgress(null);
        });
    }

    public Task StopAsync()
    {
        if (!CanStop) return Task.CompletedTask;

        return RunTransportAsync(async () =>
        {
            // The player detaches the active output even if resource cleanup fails.
            audioState = AudioState.Inactive;
            CurrentSong = null;
            SetProgress(null);
            await stop.ExecuteAsync(lifetime.Token);
            ShowStatus("Reproducción detenida.");
        });
    }

    private async Task PlayAsync(Func<CancellationToken, Task<Song?>> start, string unavailable)
    {
        await pendingVolume;
        var version = completionVersion;
        Song? song;
        try { song = await start(lifetime.Token); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A rejected selection can leave the previous track active. Check the actual output.
            await ReadProgressAsync();
            var message = error is FileNotFoundException { FileName: { } path }
                ? $"No se encontró «{Path.GetFileName(path)}». Comprueba si el archivo se movió."
                : "No se pudo reproducir el archivo. Prueba con otra canción o revisa la salida de audio.";
            ShowStatus(message, NoticeKind.PlaybackError, error.Message);
            return;
        }

        if (song is not null)
            SelectedIndex = session.CurrentIndex ?? -1;

        // A short track may finish while the start continuation is still queued.
        if (version == completionVersion)
        {
            if (song is not null)
            {
                audioState = AudioState.Playing;
                CurrentSong = song;
                SetProgress(null);
            }
            ShowStatus(song is null ? unavailable : $"Reproduciendo: {song.DisplayName}");
            if (song is not null)
                await ReadProgressAsync();
        }
    }

    public Task RefreshPlaybackProgressAsync()
    {
        if (closing || changingPlayback || audioState != AudioState.Playing || !pendingProgress.IsCompleted)
            return Task.CompletedTask;

        pendingProgress = ReadProgressAsync();
        return pendingProgress;
    }

    private void RefreshCover()
    {
        Notify(nameof(CoverSong));
        Notify(nameof(CoverLabel));
        Notify(nameof(CoverDescription));
        var path = CoverSong?.FilePath;
        if (closing || path == coverPath) return;
        coverPath = path;
        CoverImage = null;
        Notify(nameof(CoverImage));
        if (pendingCover.IsCompleted)
            pendingCover = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        // Keep at most one read in flight while the user moves through the list.
        while (!closing && coverPath is { } path)
        {
            BitmapSource? image;
            try { image = await Task.Run(() => DecodeCover(coverReader.ReadEmbeddedCover(path)), lifetime.Token); }
            catch (Exception) { image = null; } // Unreadable tags or images use the packaged artwork.
            if (closing) return;
            if (path != coverPath) continue;
            CoverImage = image;
            Notify(nameof(CoverImage));
            return;
        }
    }

    private static BitmapSource? DecodeCover(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 640;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task ReadProgressAsync()
    {
        if (closing) return;

        var version = progressVersion;
        PlaybackProgress? snapshot;
        try { snapshot = await player.GetProgressAsync(lifetime.Token); }
        catch (Exception)
        {
            // Position is optional. A failed query should not interrupt playback.
            snapshot = null;
        }

        if (!closing && version == progressVersion)
        {
            SetProgress(snapshot);
            Lyrics.ObserveProgress(snapshot);
        }
    }

    private void SetProgress(PlaybackProgress? value)
    {
        if (progress == value) return;

        progress = value;
        Notify(nameof(ProgressFraction));
        Notify(nameof(ElapsedTimeText));
        Notify(nameof(DurationText));
    }

    private static string FormatPlaybackTime(TimeSpan time) => time.TotalHours < 1
        ? time.ToString(@"mm\:ss")
        : FormattableString.Invariant($@"{(long)time.TotalHours}:{time:mm\:ss}");

    private async Task ApplyVolumeChangesAsync()
    {
        // Dragging can produce new values while the audio device handles the previous one.
        while (!closing && appliedVolumePercent != volumePercent)
        {
            var requested = volumePercent;
            try
            {
                await setVolume.ExecuteAsync(requested, lifetime.Token);
                appliedVolumePercent = requested;
            }
            catch (OperationCanceledException) when (closing) { return; }
            catch (Exception error)
            {
                if (volumePercent != requested) continue;
                volumePercent = appliedVolumePercent;
                Notify(nameof(VolumePercent));
                ShowStatus($"No se pudo ajustar el volumen: {error.Message}", NoticeKind.Error);
            }
        }
    }

    private Task RunAsync(Func<Task> action)
    {
        if (!IsReady) return Task.CompletedTask;
        pending = RunCoreAsync(action);
        return pending;
    }

    private Task RunTransportAsync(Func<Task> action) => RunAsync(async () =>
    {
        changingPlayback = true;
        progressVersion++;
        try { await action(); }
        finally { changingPlayback = false; }
    });

    private async Task RunCoreAsync(Func<Task> action)
    {
        busy = true;
        NotifyControls();
        try { await action(); }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { ShowStatus($"No se pudo completar la operación: {error.Message}", NoticeKind.Error); }
        finally { busy = false; NotifyControls(); }
    }

    private void OnPlaybackEnded(object? sender, PlaybackEndedEventArgs e)
        => context.Post(_ =>
        {
            if (!closing)
            {
                completionVersion++;
                progressVersion++;
                audioState = AudioState.Inactive;
                CurrentSong = null;
                SetProgress(null);
                NotifyControls();
                if (e.Error is not null)
                    ShowStatus("Se interrumpió la reproducción. Prueba con otra canción o revisa la salida de audio.",
                        NoticeKind.PlaybackError, e.Error.Message);
                else if (Notice is not (NoticeKind.Importing or NoticeKind.FolderError or NoticeKind.PlaybackError))
                    ShowStatus("Reproducción finalizada.");
            }
        }, null);

    public async ValueTask DisposeAsync()
    {
        if (closing) return;
        closing = true;
        NotifyControls();
        player.PlaybackEnded -= OnPlaybackEnded;
        lifetime.Cancel();
        try { await Task.WhenAll(pending, pendingVolume, pendingProgress, pendingCover, Lyrics.CloseAsync()); }
        finally { lifetime.Dispose(); }
    }

    private void ShowStatus(string message, NoticeKind kind = NoticeKind.Information, string? detail = null)
    {
        Status = message;
        Notice = kind;
        StatusDetail = detail;
        Notify(nameof(Status));
        Notify(nameof(Notice));
        Notify(nameof(StatusDetail));
    }

    private void NotifyControls()
    {
        Notify(nameof(IsReady));
        Notify(nameof(CurrentSong));
        Notify(nameof(CurrentSongIndex));
        Notify(nameof(PlaybackLabel));
        Notify(nameof(CoverLabel));
        Notify(nameof(IsPlaying));
        Notify(nameof(TransportLabel));
        Notify(nameof(CanPlaySelected));
        Notify(nameof(CanTogglePlayback));
        Notify(nameof(CanNext));
        Notify(nameof(CanPrevious));
        Notify(nameof(CanPause));
        Notify(nameof(CanResume));
        Notify(nameof(CanStop));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum AudioState { Inactive, Playing, Paused }
}
