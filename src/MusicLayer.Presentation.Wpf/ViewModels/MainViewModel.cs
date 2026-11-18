using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAudioPlayer player;
    private readonly ScanMusicFolder scanner;
    private readonly PlaybackSession session;
    private readonly PausePlayback pause;
    private readonly ResumePlayback resume;
    private readonly SynchronizationContext context;
    private readonly CancellationTokenSource lifetime = new();
    private Task pending = Task.CompletedTask;
    private bool closing;
    private bool busy;
    private AudioState audioState;
    private int completionVersion;
    private int selectedIndex = -1;
    private string status = "Elige una carpeta con música.";
    private IReadOnlyList<Song> songs = [];

    public MainViewModel(IAudioPlayer player, ScanMusicFolder scanner, PlaybackSession session, PausePlayback pause, ResumePlayback resume)
    {
        this.player = player;
        this.scanner = scanner;
        this.session = session;
        this.pause = pause;
        this.resume = resume;
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        player.PlaybackEnded += OnPlaybackEnded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value) return;
            selectedIndex = value;
            Notify();
        }
    }
    public IReadOnlyList<Song> Songs { get => songs; private set { songs = value; Notify(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool IsReady => !busy && !closing;
    public bool CanPause => IsReady && audioState == AudioState.Playing;
    public bool CanResume => IsReady && audioState == AudioState.Paused;

    public Task LoadFolderAsync(string folder) => RunAsync(async () =>
    {
        var scannedSongs = await scanner.ExecuteAsync(folder, lifetime.Token);
        await session.ReplaceQueueAsync(scannedSongs, lifetime.Token);
        Songs = scannedSongs;
        SelectedIndex = -1;
        Status = Songs.Count == 0 ? "La carpeta no contiene audio compatible." : $"{Songs.Count} canciones encontradas.";
    });

    public Task PlaySelectedAsync() => RunAsync(async () =>
    {
        var index = SelectedIndex;
        if (index < 0 || index >= Songs.Count) { Status = "Selecciona una canción."; return; }
        await PlayAsync(token => session.PlayAtAsync(index, token), "Selecciona una canción.");
    });

    public Task PlayNextAsync() => RunAsync(() =>
        PlayAsync(session.PlayNextAsync, "No hay una canción siguiente."));

    public Task PlayPreviousAsync() => RunAsync(() =>
        PlayAsync(session.PlayPreviousAsync, "No hay una canción anterior."));

    public Task PauseAsync()
    {
        if (!CanPause) return Task.CompletedTask;

        return RunAsync(async () =>
        {
            var version = completionVersion;
            var paused = await pause.ExecuteAsync(lifetime.Token);
            if (version != completionVersion) return;

            audioState = paused ? AudioState.Paused : AudioState.Inactive;
            Status = paused ? "Reproducción pausada." : "No hay audio en reproducción.";
        });
    }

    public Task ResumeAsync()
    {
        if (!CanResume) return Task.CompletedTask;

        return RunAsync(async () =>
        {
            var version = completionVersion;
            var resumed = await resume.ExecuteAsync(lifetime.Token);
            if (version != completionVersion) return;

            audioState = resumed ? AudioState.Playing : AudioState.Inactive;
            Status = resumed ? "Reproducción reanudada." : "No hay audio en pausa.";
        });
    }

    private async Task PlayAsync(Func<CancellationToken, Task<Song?>> start, string unavailable)
    {
        var version = completionVersion;
        var song = await start(lifetime.Token);
        if (song is not null)
            SelectedIndex = session.CurrentIndex ?? -1;

        // A short track may finish while the start continuation is still queued.
        if (version == completionVersion)
        {
            if (song is not null)
                audioState = AudioState.Playing;
            Status = song is null ? unavailable : $"Reproduciendo: {song.DisplayName}";
        }
    }

    private Task RunAsync(Func<Task> action)
    {
        if (!IsReady) return Task.CompletedTask;
        pending = RunCoreAsync(action);
        return pending;
    }

    private async Task RunCoreAsync(Func<Task> action)
    {
        busy = true;
        NotifyControls();
        try { await action(); }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { Status = $"No se pudo completar la operación: {error.Message}"; }
        finally { busy = false; NotifyControls(); }
    }

    private void OnPlaybackEnded(object? sender, PlaybackEndedEventArgs e)
        => context.Post(_ =>
        {
            if (!closing)
            {
                completionVersion++;
                audioState = AudioState.Inactive;
                Notify(nameof(CanPause));
                Notify(nameof(CanResume));
                Status = e.Error is null ? "Reproducción finalizada." : $"Error de reproducción: {e.Error.Message}";
            }
        }, null);

    public async ValueTask DisposeAsync()
    {
        if (closing) return;
        closing = true;
        NotifyControls();
        player.PlaybackEnded -= OnPlaybackEnded;
        lifetime.Cancel();
        try { await pending; }
        finally { lifetime.Dispose(); }
    }

    private void NotifyControls()
    {
        Notify(nameof(IsReady));
        Notify(nameof(CanPause));
        Notify(nameof(CanResume));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum AudioState { Inactive, Playing, Paused }
}
