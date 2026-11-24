using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Lyrics;
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
    private readonly SeekPlayback seek;
    private readonly ICoverArtReader coverReader;
    private readonly IPlaybackPreferencesRepository preferences;
    private readonly SynchronizationContext context;
    private readonly CancellationTokenSource lifetime = new();
    private Task pending = Task.CompletedTask;
    private Task pendingVolume = Task.CompletedTask;
    private Task pendingVolumeSave = Task.CompletedTask;
    private Task pendingOptionsSave = Task.CompletedTask;
    private Task pendingFavoritesSave = Task.CompletedTask;
    private PlayerOptions options = new();
    private PlaybackBookmark? resumeBookmark;
    private Task pendingProgress = Task.CompletedTask;
    private Task pendingCover = Task.CompletedTask;
    private string? coverPath;
    private bool closing;
    private bool busy;
    private bool changingPlayback;
    private bool startingTrack;
    private bool advanceAfterCompletion;
    private Func<CancellationToken, Task<Song?>>? retryPlayback;
    private bool seeking;
    private AudioState audioState;
    private int completionVersion;
    private string? completedFile;
    private int progressVersion;
    private PlaybackProgress? progress;
    private int selectedIndex = -1;
    private int volumePercent = IAudioPlayer.DefaultVolumePercent;
    private int appliedVolumePercent = IAudioPlayer.DefaultVolumePercent;
    private IReadOnlyList<Song> songs = [];

    public MainViewModel(IAudioPlayer player, ScanMusicFolder scanner, PlaybackSession session,
        PausePlayback pause, ResumePlayback resume, StopPlayback stop, SetPlaybackVolume setVolume, ICoverArtReader coverReader,
        LoadTrackLyrics loadLyrics, SeekPlayback seek, IPlaybackPreferencesRepository preferences,
        IPlaylistRepository? playlistRepository = null)
    {
        this.player = player;
        this.scanner = scanner;
        this.session = session;
        this.pause = pause;
        this.resume = resume;
        this.stop = stop;
        this.setVolume = setVolume;
        this.seek = seek;
        this.coverReader = coverReader;
        this.preferences = preferences;
        Library.FavoriteChanged += QueueFavoriteSave;
        if (playlistRepository is not null) Playlists = new PlaylistsViewModel(playlistRepository, lifetime.Token);
        Lyrics = new LyricsViewModel(loadLyrics, position => RunSeekAsync(_ => seek.ExecuteAsync(position, lifetime.Token)));
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        player.PlaybackEnded += OnPlaybackEnded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public LyricsViewModel Lyrics { get; }
    public LibraryBrowser Library { get; } = new();
    public PlaylistsViewModel? Playlists { get; }
    public string? LibraryFolder { get; private set; }
    public SongRow? SelectedLibraryRow
    {
        get => SelectedIndex >= 0 && SelectedIndex < SongRows.Count ? SongRows[SelectedIndex] : null;
        set { if (value is not null) SelectedIndex = value.Index; }
    }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value) return;
            selectedIndex = value;
            Notify(nameof(SelectedLibraryRow));
            Notify(nameof(SelectedQueueRow));
            Notify();
            RefreshCover();
            Notify(nameof(CanPlaySelected));
            Notify(nameof(CanPlayQueueSelected));
            Notify(nameof(CanTogglePlayback));
        }
    }
    public IReadOnlyList<Song> Songs { get => songs; private set { songs = value; Notify(); } }
    public IReadOnlyList<SongRow> SongRows { get; private set; } = [];
    public IReadOnlyList<SongRow> QueueRows { get; private set; } = [];
    public SongRow? SelectedQueueRow
    {
        get => SelectedLibraryRow is { } row && (ReferenceEquals(QueueRows, SongRows) || QueueRows.Contains(row)) ? row : null;
        set { if (value is not null) SelectedIndex = value.Index; }
    }
    public string Status { get; private set; } = "Elige una carpeta con música.";
    public string? StatusDetail { get; private set; }
    public string? PreferencesWarning { get; private set; }
    public NoticeKind Notice { get; private set; }
    public bool CanRetryPlayback => IsReady && Notice == NoticeKind.PlaybackError && retryPlayback is not null;
    public bool HasImportedFolder { get; private set; }
    public bool IsReady => !busy && !closing;
    public Song? CurrentSong
    {
        get;
        private set { field = value; Lyrics.ChangeTrack(value); RefreshCover(); }
    }
    public Song? CoverSong => CurrentSong ?? (SelectedIndex >= 0 && SelectedIndex < Songs.Count ? Songs[SelectedIndex] : null);
    public SongRow? CoverRow => CurrentSong is { } song
        ? SongRows.FirstOrDefault(row => StringComparer.OrdinalIgnoreCase.Equals(row.Song.FilePath, song.FilePath))
        : SelectedLibraryRow;
    public string CoverLabel => CurrentSong is null && CoverSong is not null ? "Selección" : PlaybackLabel;
    public string? CoverDescription => CoverSong?.Metadata switch
    {
        { Artist: { } artist, Album: { } album } => $"{artist} · {album}",
        { Artist: { } artist } => artist,
        { Album: { } album } => album,
        _ => CoverSong?.FolderName
    };
    public string CoverAudioDetails => string.Join(" · ", new[]
    {
        CoverSong?.Metadata?.AudioBitrate is int bitrate ? $"{bitrate} kb/s" : null,
        CoverSong?.Metadata?.AudioSampleRate is int hz ? $"{hz / 1000d:0.###} kHz" : null
    }.OfType<string>());
    public BitmapSource? CoverImage { get; private set; }
    // Preserve the current tint until the new artwork read resolves, including a missing cover.
    public BitmapSource? AmbientCoverImage { get; private set; }
    public int CurrentQueueIndex => CurrentSong is null ? -1 : session.CurrentIndex ?? -1;
    public int CurrentSongIndex
    {
        get
        {
            var index = CurrentQueueIndex;
            return index >= 0 && index < QueueRows.Count ? QueueRows[index].Index : -1;
        }
    }
    public bool CanPlaySelected => IsReady && SelectedIndex >= 0 && SelectedIndex < Songs.Count;
    public bool CanPlayQueueSelected => IsReady && SelectedQueueRow is not null;
    public bool CanTogglePlayback => CanStop || CanPlaySelected || CanNext;
    public bool CanNext => IsReady && HasNextInMode;
    private bool HasNextInMode => IsShuffleEnabled ? session.HasRandomNext : session.HasNext;
    public bool IsShuffleEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Notify();
            Notify(nameof(CanNext));
            Notify(nameof(CanTogglePlayback));
        }
    }
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
    public bool CanSeek => !closing && (!busy || seeking) && CurrentSong is not null && progress is { Duration.Ticks: > 0 };
    public double ProgressFraction => progress is { } value
        ? value.Elapsed.TotalSeconds / value.Duration.TotalSeconds
        : 0;
    public string ElapsedTimeText => FormatPlaybackTime(progress?.Elapsed ?? TimeSpan.Zero);
    public string DurationText => progress is { } value ? FormatPlaybackTime(value.Duration) : "--:--";

    public bool RememberPosition
    {
        get => options.RememberPosition;
        set => ChangeOptions(options with { RememberPosition = value });
    }

    public bool AutoAdvance
    {
        get => options.AutoAdvance;
        set => ChangeOptions(options with { AutoAdvance = value });
    }

    public bool FollowLyrics
    {
        get => options.FollowLyrics;
        set => ChangeOptions(options with { FollowLyrics = value });
    }

    public bool DimInactiveLyrics
    {
        get => options.DimInactiveLyrics;
        set => ChangeOptions(options with { DimInactiveLyrics = value });
    }

    private void ChangeOptions(PlayerOptions value, [CallerMemberName] string? property = null)
    {
        if (closing || options == value) return;
        options = value;
        if (!value.RememberPosition) resumeBookmark = null;
        Notify(property);
        if (pendingOptionsSave.IsCompleted) pendingOptionsSave = SaveOptionsChangesAsync();
    }

    private async Task SaveOptionsChangesAsync()
    {
        PlayerOptions snapshot;
        do
        {
            snapshot = options;
            await SavePreferenceAsync(() => preferences.SaveOptionsAsync(snapshot), "No se pudieron guardar los ajustes.");
        } while (snapshot != options);
    }

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

    public Task InitializeAsync() => RunAsync(async () =>
    {
        if (Playlists is { } playlists) await playlists.InitializeAsync();
        PlaybackPreferences saved;
        try { saved = await preferences.ReadAsync(lifetime.Token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WarnAboutPreferences("No se pudieron cargar las preferencias guardadas.");
            return;
        }

        try { Library.RestoreFavorites(await preferences.ReadFavoritesAsync(lifetime.Token)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WarnAboutPreferences("No se pudieron cargar las canciones favoritas.");
        }

        options = saved.Options;
        Notify(nameof(RememberPosition));
        Notify(nameof(AutoAdvance));
        Notify(nameof(FollowLyrics));
        Notify(nameof(DimInactiveLyrics));

        if (saved.VolumePercent != appliedVolumePercent)
        {
            try
            {
                await setVolume.ExecuteAsync(saved.VolumePercent, lifetime.Token);
                appliedVolumePercent = volumePercent = saved.VolumePercent;
                Notify(nameof(VolumePercent));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                WarnAboutPreferences("No se pudo aplicar el volumen guardado.");
            }
        }

        lifetime.Token.ThrowIfCancellationRequested();
        if (saved.LibraryFolder is { } folder)
        {
            await LoadFolderCoreAsync(folder, restoring: true);
            if (RememberPosition && saved.Bookmark is { } bookmark &&
                SongRows.FirstOrDefault(row => StringComparer.OrdinalIgnoreCase.Equals(row.Song.FilePath, bookmark.FilePath)) is { } row)
            {
                SelectedIndex = row.Index;
                resumeBookmark = bookmark;
                ShowStatus($"Lista para continuar: {row.Song.DisplayName} · {FormatPlaybackTime(bookmark.Position)}");
            }
        }
    });

    public Task LoadFolderAsync(string folder) => RunAsync(() => LoadFolderCoreAsync(folder, restoring: false));

    private async Task LoadFolderCoreAsync(string folder, bool restoring)
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
            var message = restoring
                ? "No se pudo abrir la carpeta guardada. Comprueba el acceso o elige otra."
                : "No se pudo leer la carpeta. Comprueba el acceso o elige otra.";
            ShowStatus(message, NoticeKind.FolderError, error.Message);
            return;
        }
        HasImportedFolder = true;
        resumeBookmark = null;
        Notify(nameof(HasImportedFolder));
        Songs = scannedSongs;
        SongRows = scannedSongs.Select((song, index) => new SongRow(song, index)).ToArray();
        Notify(nameof(SongRows));
        SetQueueRows(SongRows);
        LibraryFolder = folder;
        Notify(nameof(LibraryFolder));
        Library.ReplaceRows(SongRows);
        Playlists?.SetLibrary(SongRows);
        SelectedIndex = -1;
        Notify(nameof(CoverRow));
        if (Songs.Count == 0)
            ShowStatus("La carpeta no contiene audio compatible.");
        else
            ShowStatus(Songs.Count == 1 ? "1 canción encontrada." : $"{Songs.Count} canciones encontradas.", NoticeKind.Imported);

        if (!restoring)
            await SavePreferenceAsync(() => preferences.SaveFolderAsync(folder), "No se pudo guardar la carpeta importada.");
    }

    public Task PlaySelectedAsync() => RunTransportAsync(async () =>
    {
        var index = SelectedIndex;
        if (index < 0 || index >= Songs.Count) { ShowStatus("Selecciona una canción."); return; }
        await PlayAsync(token => ReferenceEquals(QueueRows, SongRows)
            ? session.PlayAtAsync(index, token)
            : StartQueueAsync(SongRows, index, token), "Selecciona una canción.");
    });

    public Task PlayQueueRowAsync(SongRow row) => RunTransportAsync(async () =>
    {
        for (var index = 0; index < QueueRows.Count; index++)
        {
            if (QueueRows[index] != row) continue;
            var queueIndex = index;
            await PlayAsync(token => session.PlayAtAsync(queueIndex, token), "La pista ya no está en la cola.");
            return;
        }
    });

    public Task PlayPlaylistAsync(IReadOnlyList<SongRow> rows, int startIndex) => RunTransportAsync(() =>
        startIndex >= 0 && startIndex < rows.Count
            ? PlayAsync(token => StartQueueAsync(rows, startIndex, token), "La playlist no tiene canciones disponibles.")
            : Task.CompletedTask);

    private async Task<Song?> StartQueueAsync(IReadOnlyList<SongRow> rows, int index, CancellationToken token)
    {
        var song = await session.PlayQueueAsync(rows.Select(row => row.Song).ToArray(), index, token);
        if (song is not null) SetQueueRows(rows);
        return song;
    }

    private void SetQueueRows(IReadOnlyList<SongRow> rows)
    {
        QueueRows = rows;
        Notify(nameof(QueueRows));
        Notify(nameof(SelectedQueueRow));
        Notify(nameof(CanPlayQueueSelected));
    }

    public Task PlayNextAsync() => RunTransportAsync(PlayNextInModeAsync);

    private Task PlayNextInModeAsync()
    {
        if (!IsShuffleEnabled)
            return PlayAsync(session.PlayNextAsync, "No hay una canción siguiente.");

        return session.ChooseRandomNextIndex() is int index
            ? PlayAsync(token => session.PlayAtAsync(index, token), "No hay otra canción en la cola.")
            : Task.CompletedTask;
    }

    public Task RetryPlaybackAsync() => CanRetryPlayback && retryPlayback is { } start
        ? RunTransportAsync(() => PlayAsync(start, "La pista ya no está en la cola."))
        : Task.CompletedTask;

    public Task TogglePlaybackAsync() => audioState switch
    {
        AudioState.Playing => PauseAsync(),
        AudioState.Paused => ResumeAsync(),
        _ when SelectedQueueRow is { } row => PlayQueueRowAsync(row),
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
        startingTrack = true;
        try { song = await start(lifetime.Token); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            advanceAfterCompletion = false;
            retryPlayback = start;
            // A rejected selection can leave the previous track active. Check the actual output.
            await ReadProgressAsync();
            var message = error is FileNotFoundException { FileName: { } path }
                ? $"No se encontró «{Path.GetFileName(path)}». Comprueba si el archivo se movió."
                : "No se pudo reproducir el archivo. Prueba con otra canción o revisa la salida de audio.";
            ShowStatus(message, NoticeKind.PlaybackError, error.Message);
            return;
        }
        finally { startingTrack = false; }

        if (song is not null)
        {
            SelectedIndex = session.CurrentIndex is int index ? QueueRows[index].Index : -1;
            Library.RecordPlay(song);
        }

        // An outgoing track can finish while the requested replacement is starting.
        var replacedFinishedTrack = song is not null && completedFile is not null &&
            !StringComparer.OrdinalIgnoreCase.Equals(completedFile, song.FilePath);
        if (version == completionVersion || replacedFinishedTrack)
        {
            advanceAfterCompletion = false;
            if (song is not null)
            {
                audioState = AudioState.Playing;
                CurrentSong = song;
                SetProgress(null);
            }
            ShowStatus(song is null ? unavailable : $"Reproduciendo: {song.DisplayName}");
            if (song is not null)
            {
                var bookmark = resumeBookmark;
                resumeBookmark = null;
                await ReadProgressAsync();
                if (bookmark is not null && CurrentSong == song && progress is { } clock &&
                    StringComparer.OrdinalIgnoreCase.Equals(song.FilePath, bookmark.FilePath) &&
                    bookmark.Position > TimeSpan.Zero && bookmark.Position < clock.Duration)
                {
                    var moved = await seek.ExecuteAsync(bookmark.Position, lifetime.Token);
                    if (CurrentSong == song && moved is { } snapshot) ApplyProgress(snapshot);
                }
            }
        }
    }

    public Task RefreshPlaybackProgressAsync()
    {
        if (closing || changingPlayback || audioState != AudioState.Playing || !pendingProgress.IsCompleted)
            return Task.CompletedTask;

        pendingProgress = ReadProgressAsync();
        return pendingProgress;
    }

    public Task SeekAsync(double fraction)
        => RunSeekAsync(duration => seek.ExecuteAsync(fraction, duration, lifetime.Token));

    private Task RunSeekAsync(Func<TimeSpan, Task<PlaybackProgress?>> move)
    {
        if (!IsReady || !CanSeek || progress is not { } current) return Task.CompletedTask;

        seeking = true;
        return RunTransportAsync(async () =>
        {
            var version = progressVersion;
            try
            {
                var snapshot = await move(current.Duration);
                if (!closing && version == progressVersion && snapshot is { } position)
                    ApplyProgress(position);
            }
            finally { seeking = false; }
        });
    }

    private void RefreshCover()
    {
        Notify(nameof(CoverSong));
        Notify(nameof(CoverRow));
        Notify(nameof(CoverLabel));
        Notify(nameof(CoverDescription));
        Notify(nameof(CoverAudioDetails));
        var path = CoverSong?.FilePath;
        if (closing || path == coverPath) return;
        coverPath = path;
        CoverImage = null;
        Notify(nameof(CoverImage));
        if (path is null)
            SetAmbientCover(null);
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
            SetAmbientCover(image);
            return;
        }
    }

    private void SetAmbientCover(BitmapSource? image)
    {
        AmbientCoverImage = image;
        Notify(nameof(AmbientCoverImage));
    }

    internal Task<BitmapSource?> ReadThumbnailAsync(Song song, CancellationToken token)
        => Task.Run(() => DecodeCover(coverReader.ReadEmbeddedCover(song.FilePath), 72), token);

    internal static BitmapSource? DecodeCover(byte[]? bytes, int width = 640)
    {
        if (bytes is not { Length: > 0 }) return null;
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width;
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
            ApplyProgress(snapshot);
    }

    private void ApplyProgress(PlaybackProgress? snapshot)
    {
        SetProgress(snapshot);
        Lyrics.ObserveProgress(snapshot);
    }

    private void SetProgress(PlaybackProgress? value)
    {
        if (progress == value) return;

        var couldSeek = CanSeek;
        progress = value;
        Notify(nameof(ProgressFraction));
        Notify(nameof(ElapsedTimeText));
        Notify(nameof(DurationText));
        if (couldSeek != CanSeek) Notify(nameof(CanSeek));
    }

    internal static string FormatPlaybackTime(TimeSpan time) => time.TotalHours < 1
        ? time.ToString(@"mm\:ss")
        : FormattableString.Invariant($@"{(long)time.TotalHours}:{time:mm\:ss}");

    private async Task ApplyVolumeChangesAsync()
    {
        // Dragging can produce new values while the audio device handles the previous one.
        while (appliedVolumePercent != volumePercent)
        {
            var requested = volumePercent;
            try
            {
                // Apply the last slider value even if the window is already closing.
                await setVolume.ExecuteAsync(requested);
                appliedVolumePercent = requested;
                if (pendingVolumeSave.IsCompleted)
                    pendingVolumeSave = SaveVolumeChangesAsync();
            }
            catch (Exception error)
            {
                if (volumePercent != requested) continue;
                volumePercent = appliedVolumePercent;
                Notify(nameof(VolumePercent));
                ShowStatus($"No se pudo ajustar el volumen: {error.Message}", NoticeKind.Error);
            }
        }
    }

    private async Task SaveVolumeChangesAsync()
    {
        int applied;
        do
        {
            applied = appliedVolumePercent;
            await SavePreferenceAsync(() => preferences.SaveVolumeAsync(applied), "No se pudo guardar el volumen.");
        } while (applied != appliedVolumePercent);
    }

    private async Task SavePreferenceAsync(Func<Task> save, string warning)
    {
        try { await save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WarnAboutPreferences(warning);
        }
    }

    private void QueueFavoriteSave(string path, bool isFavorite)
        => pendingFavoritesSave = SaveFavoriteAfterAsync(pendingFavoritesSave, path, isFavorite);

    private async Task SaveFavoriteAfterAsync(Task previous, string path, bool isFavorite)
    {
        await previous;
        await SavePreferenceAsync(() => preferences.SaveFavoriteAsync(path, isFavorite),
            "No se pudo guardar el cambio en Favoritas.");
    }

    private void WarnAboutPreferences(string message)
    {
        PreferencesWarning = message;
        Notify(nameof(PreferencesWarning));
    }

    private Task RunAsync(Func<Task> action)
    {
        if (!IsReady) return Task.CompletedTask;
        pending = RunCoreAsync(action);
        return pending;
    }

    private Task RunTransportAsync(Func<Task> action) => RunAsync(() => ChangePlaybackAsync(action));

    private async Task ChangePlaybackAsync(Func<Task> action)
    {
        changingPlayback = true;
        progressVersion++;
        try { await action(); }
        finally { changingPlayback = false; }
    }

    private async Task RunCoreAsync(Func<Task> action)
    {
        busy = true;
        NotifyControls();
        try
        {
            await action();
            while (!closing && advanceAfterCompletion)
                await AdvanceAfterCompletionAsync();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { ShowStatus($"No se pudo completar la operación: {error.Message}", NoticeKind.Error); }
        finally { busy = false; NotifyControls(); }
    }

    private void OnPlaybackEnded(object? sender, PlaybackEndedEventArgs e)
        => context.Post(_ =>
        {
            if (closing) return;
            if (e.Error is null && !startingTrack && CurrentSong is { } current && e.FilePath is { } ended &&
                !StringComparer.OrdinalIgnoreCase.Equals(current.FilePath, ended))
                return;

            advanceAfterCompletion = e.Error is null &&
                (startingTrack || (CurrentSong is not null && session.CurrentIndex is not null));
            completedFile = e.Error is null ? e.FilePath : null;
            completionVersion++;
            progressVersion++;
            audioState = AudioState.Inactive;
            CurrentSong = null;
            SetProgress(null);
            NotifyControls();
            if (e.Error is not null)
            {
                ShowStatus("Se interrumpió la reproducción. Prueba con otra canción o revisa la salida de audio.",
                    NoticeKind.PlaybackError, e.Error.Message);
            }
            else if (!advanceAfterCompletion)
            {
                ShowPlaybackFinished();
            }
            else if (!busy)
            {
                _ = RunAsync(AdvanceAfterCompletionAsync);
            }
        }, null);

    private Task AdvanceAfterCompletionAsync()
    {
        advanceAfterCompletion = false;
        if (AutoAdvance && session.CurrentIndex is not null && HasNextInMode)
            return ChangePlaybackAsync(PlayNextInModeAsync);

        ShowPlaybackFinished();
        return Task.CompletedTask;
    }

    private void ShowPlaybackFinished()
    {
        if (Notice is not (NoticeKind.Importing or NoticeKind.FolderError or NoticeKind.PlaybackError))
            ShowStatus("Reproducción finalizada.");
    }

    public async ValueTask DisposeAsync()
    {
        if (closing) return;
        closing = true;
        NotifyControls();
        player.PlaybackEnded -= OnPlaybackEnded;
        Library.FavoriteChanged -= QueueFavoriteSave;
        lifetime.Cancel();
        try
        {
            await Task.WhenAll(pending, pendingVolume, pendingProgress, pendingCover, Lyrics.CloseAsync());
            if (Playlists is { } playlists) await playlists.Pending;
            await Task.WhenAll(pendingVolumeSave, pendingOptionsSave, pendingFavoritesSave);
            if (RememberPosition && HasImportedFolder)
            {
                var bookmark = CurrentSong is { } song
                    ? new PlaybackBookmark(song.FilePath, progress?.Elapsed ?? TimeSpan.Zero) : resumeBookmark;
                await SavePreferenceAsync(() => preferences.SaveBookmarkAsync(bookmark), "No se pudo guardar el punto de reproducción.");
            }
        }
        finally { lifetime.Dispose(); }
    }

    private void ShowStatus(string message, NoticeKind kind = NoticeKind.Information, string? detail = null)
    {
        if (kind != NoticeKind.PlaybackError) retryPlayback = null;
        Status = message;
        Notice = kind;
        StatusDetail = detail;
        Notify(nameof(Status));
        Notify(nameof(Notice));
        Notify(nameof(StatusDetail));
        Notify(nameof(CanRetryPlayback));
    }

    private void NotifyControls()
    {
        Notify(nameof(IsReady));
        Notify(nameof(CurrentSong));
        Notify(nameof(CurrentSongIndex));
        Notify(nameof(CurrentQueueIndex));
        Notify(nameof(PlaybackLabel));
        Notify(nameof(CoverLabel));
        Notify(nameof(IsPlaying));
        Notify(nameof(TransportLabel));
        Notify(nameof(CanPlaySelected));
        Notify(nameof(CanPlayQueueSelected));
        Notify(nameof(CanRetryPlayback));
        Notify(nameof(CanTogglePlayback));
        Notify(nameof(CanNext));
        Notify(nameof(CanPrevious));
        Notify(nameof(CanPause));
        Notify(nameof(CanResume));
        Notify(nameof(CanStop));
        Notify(nameof(CanSeek));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum AudioState { Inactive, Playing, Paused }
}
