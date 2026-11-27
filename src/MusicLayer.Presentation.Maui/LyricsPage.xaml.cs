using System.ComponentModel;
using AndroidX.RecyclerView.Widget;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Presentation.Maui.Platforms.Android;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Maui;

public partial class LyricsPage : ContentPage
{
    private readonly AndroidPlayback playback;
    private readonly LocalLrcStore local;
    private readonly LyricsViewModel model;
    private readonly IDispatcherTimer clock;
    private LibraryTrack? displayedTrack;
    private long followAfter;
    private int followedIndex = -1;
    private int shownSecond = -1;
    private bool importing;

    internal event EventHandler? PlaybackRequested;
    internal event EventHandler? SearchRequested;
    internal event EventHandler? ImportRequested;

    public LyricsPage(AndroidPlayback playback, LoadTrackLyrics loadLyrics, LocalLrcStore local)
    {
        InitializeComponent();
        this.playback = playback;
        this.local = local;
        model = new LyricsViewModel(loadLyrics, time =>
        {
            playback.Seek((int)Math.Clamp(time.TotalMilliseconds, 0, playback.Duration), precise: true);
            return Task.CompletedTask;
        });
        BindingContext = model;
        MiniPlayer.Connect(playback);
        clock = Dispatcher.CreateTimer();
        clock.Interval = TimeSpan.FromMilliseconds(180);
        clock.Tick += (_, _) => UpdateLyricsPosition();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        model.PropertyChanged += OnLyricsChanged;
        playback.Changed += RefreshTrack;
        SetTrack(playback.Current);
        model.IsOpen = true;
        UpdateLyricsPosition();
        clock.Start();
    }

    protected override void OnDisappearing()
    {
        clock.Stop();
        playback.Changed -= RefreshTrack;
        model.PropertyChanged -= OnLyricsChanged;
        model.IsOpen = false;
        base.OnDisappearing();
    }

    private void RefreshTrack(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(displayedTrack, playback.Current)) SetTrack(playback.Current);
        UpdateLyricsPosition();
    }

    private void SetTrack(LibraryTrack? track)
    {
        displayedTrack = track;
        followedIndex = shownSecond = -1;
        followAfter = 0;
        TrackCard.IsVisible = track is not null;
        Cover.Source = track?.ArtworkPath ?? "sixteenth_notes.png";
        TrackTitle.Text = track?.Title;
        Credits.Text = track?.Credits;
        model.ChangeTrack(track?.Row.Song, track?.ContentUri);
    }

    private void UpdateLyricsPosition()
    {
        if (displayedTrack is null) return;
        if (VerseList.Handler?.PlatformView is RecyclerView { ScrollState: RecyclerView.ScrollStateDragging })
            followAfter = Environment.TickCount64 + 6000;

        int elapsed = playback.Position;
        int duration = playback.Duration;
        if (duration > 0)
            model.ObserveProgress(new PlaybackProgress(TimeSpan.FromMilliseconds(elapsed), TimeSpan.FromMilliseconds(duration)));
        else if (!playback.IsPreparing)
            model.ObserveProgress(null);

        if (!VerseList.IsVisible) return;
        int second = elapsed / 1000;
        if (second != shownSecond)
        {
            shownSecond = second;
            ReadingHint.Text = $"Tocá un verso para saltar en la canción ({second / 60}:{second % 60:00})";
        }

        int index = model.ActiveLineIndex;
        if (VerseList.Height <= 0 || Environment.TickCount64 < followAfter || index < 0 || index == followedIndex) return;
        VerseList.ScrollTo(index, position: ScrollToPosition.Start, animate: followedIndex >= 0 && index == followedIndex + 1);
        followedIndex = index;
    }

    private void ReadVersesManually(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (VerseList.Handler?.PlatformView is not RecyclerView { ScrollState: RecyclerView.ScrollStateDragging }) return;
        followAfter = Environment.TickCount64 + 6000;
        followedIndex = -1;
    }

    private async void SeekToVerse(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LyricRow row }) return;
        await model.SeekToLineAsync(row);
        followAfter = 0;
        followedIndex = -1;
    }

    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LyricsViewModel.Status) or nameof(LyricsViewModel.CanRetry))) return;
        var kind = model.DocumentKind;
        bool synchronized = kind == LyricsDocumentKind.Synchronized;
        bool plain = kind == LyricsDocumentKind.Plain;
        bool loading = displayedTrack is not null && kind is null;
        VerseList.IsVisible = ReadingHint.IsVisible = synchronized;
        PlainLyrics.IsVisible = plain;
        StatePanel.IsVisible = !synchronized && !plain;
        Loading.IsVisible = Loading.IsRunning = loading;
        StateIcon.IsVisible = !loading;
        Mode.Text = kind switch
        {
            LyricsDocumentKind.Synchronized => "SÍNCRONAS",
            LyricsDocumentKind.Plain => "SIN TIEMPOS",
            LyricsDocumentKind.Instrumental => "INSTRUMENTAL",
            null => "BUSCANDO",
            _ => "SIN LETRA"
        };
        SourceLabel.Text = model.UsesLocalFile ? "LRC local" : model.HasLyrics ? "LRCLIB" : "LRCLIB · LRC";
        StateMessage.Text = kind switch
        {
            LyricsDocumentKind.Instrumental => "Esta pista está marcada como instrumental.",
            LyricsDocumentKind.TemporarilyUnavailable => "No se pudo consultar LRCLIB. Reintentá o añadí un archivo .lrc.",
            LyricsDocumentKind.NotFound => "No se encontró una letra para esta canción. Podés buscarla de nuevo o añadirla manualmente.",
            _ when loading => "Buscando un archivo LRC o una coincidencia en LRCLIB.",
            _ => ""
        };
        StateMessage.IsVisible = StateMessage.Text.Length > 0;
        Retry.IsVisible = model.CanRetry;
        Retry.Text = kind == LyricsDocumentKind.TemporarilyUnavailable ? "Reintentar" : "Buscar letra";
        AddLrc.IsVisible = displayedTrack?.ContentUri is not null && !loading;
        StateActions.IsVisible = Retry.IsVisible || AddLrc.IsVisible;
    }

    private async void RetryLookup(object? sender, EventArgs e) => await model.RetryAsync();

    private async void OpenLyricsOptions(object? sender, EventArgs e)
    {
        if (displayedTrack is not { ContentUri: not null } track || importing) return;
        var result = await DisplayActionSheetAsync("Opciones de letra", "Cancelar", null, "Añadir archivo .lrc");
        if (result == "Añadir archivo .lrc") await ImportLrcAsync(track);
    }

    private async void AddLyrics(object? sender, EventArgs e) => await ImportLrcAsync(displayedTrack);

    private async Task ImportLrcAsync(LibraryTrack? track)
    {
        if (importing || track?.ContentUri is not { } key) return;
        importing = true;
        try
        {
            var activity = Platform.CurrentActivity as MainActivity
                ?? throw new IOException("No hay un selector disponible.");
            if (await activity.PickLrcAsync() is not { } file) return;
            await Task.Run(() => local.ImportAsync(key, file));
            if (model.IsOpen && ReferenceEquals(playback.Current, track))
            {
                SetTrack(track);
                UpdateLyricsPosition();
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException error)
        {
            if (model.IsOpen) await DisplayAlertAsync("No se pudo añadir la letra", error.Message, "Aceptar");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Java.IO.IOException or Java.Lang.SecurityException)
        {
            if (model.IsOpen)
                await DisplayAlertAsync("No se pudo añadir la letra", "Volvé a elegir un archivo .lrc que se pueda leer.", "Aceptar");
        }
        finally { importing = false; }
    }

    private void OpenPlayback(object? sender, EventArgs e) => PlaybackRequested?.Invoke(this, e);
    private void SearchLibrary(object? sender, EventArgs e) => SearchRequested?.Invoke(this, e);
    private void ImportMusic(object? sender, EventArgs e) => ImportRequested?.Invoke(this, e);
}
