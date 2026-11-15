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
    private readonly PlaySong play;
    private readonly SynchronizationContext context;
    private readonly CancellationTokenSource lifetime = new();
    private Task pending = Task.CompletedTask;
    private bool closing;
    private bool busy;
    private string status = "Elige una carpeta con música.";
    private IReadOnlyList<Song> songs = [];

    public MainViewModel(IAudioPlayer player, ScanMusicFolder scanner, PlaySong play)
    {
        this.player = player;
        this.scanner = scanner;
        this.play = play;
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        player.PlaybackEnded += OnPlaybackEnded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Song? SelectedSong { get; set; }
    public IReadOnlyList<Song> Songs { get => songs; private set { songs = value; Notify(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool IsReady => !busy && !closing;

    public Task LoadFolderAsync(string folder) => RunAsync(async () =>
    {
        Songs = await scanner.ExecuteAsync(folder, lifetime.Token);
        SelectedSong = null;
        Notify(nameof(SelectedSong));
        Status = Songs.Count == 0 ? "La carpeta no contiene audio compatible." : $"{Songs.Count} canciones encontradas.";
    });

    public Task PlaySelectedAsync() => RunAsync(async () =>
    {
        if (SelectedSong is not Song song) { Status = "Selecciona una canción."; return; }
        Status = $"Reproduciendo: {song.DisplayName}";
        await play.ExecuteAsync(song, lifetime.Token);
    });

    private Task RunAsync(Func<Task> action)
    {
        if (!IsReady) return Task.CompletedTask;
        pending = RunCoreAsync(action);
        return pending;
    }

    private async Task RunCoreAsync(Func<Task> action)
    {
        busy = true;
        Notify(nameof(IsReady));
        try { await action(); }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { Status = $"No se pudo completar la operación: {error.Message}"; }
        finally { busy = false; Notify(nameof(IsReady)); }
    }

    private void OnPlaybackEnded(object? sender, PlaybackEndedEventArgs e)
        => context.Post(_ =>
        {
            if (!closing)
                Status = e.Error is null ? "Reproducción finalizada." : $"Error de reproducción: {e.Error.Message}";
        }, null);

    public async ValueTask DisposeAsync()
    {
        if (closing) return;
        closing = true;
        Notify(nameof(IsReady));
        player.PlaybackEnded -= OnPlaybackEnded;
        lifetime.Cancel();
        try { await pending; }
        finally { lifetime.Dispose(); }
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
