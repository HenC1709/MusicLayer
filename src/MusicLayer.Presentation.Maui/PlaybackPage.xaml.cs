using MusicLayer.Presentation.Maui.Platforms.Android;
using MusicLayer.Shared.Metadata;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Maui;

public sealed class PlaybackQueueRow(LibraryTrack track) : BindableObject
{
    public LibraryTrack Track { get; } = track;
    public string Title => Track.Title;
    public string Credits => Track.Credits;
    public string Artwork => Track.ArtworkPath ?? "sixteenth_notes.png";
    public string Duration => Track.Row.DurationText;
    public bool IsCurrent
    {
        get;
        internal set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }
}

public partial class PlaybackPage : ContentPage
{
    private readonly AndroidPlayback playback;
    private readonly TagLibTrackMetadataReader metadata;
    private readonly IDispatcherTimer clock;
    private LibraryTrack? displayedTrack;
    private CancellationTokenSource? artworkRequest;
    private bool seeking;
    private bool updatingPosition;
    private IReadOnlyList<LibraryTrack>? displayedQueue;
    private PlaybackQueueRow[] queueRows = [];
    private PlaybackQueueRow? activeQueueRow;
    internal event EventHandler? LibraryRequested;

    public PlaybackPage(AndroidPlayback playback, TagLibTrackMetadataReader metadata)
    {
        InitializeComponent();
        this.playback = playback;
        this.metadata = metadata;
        clock = Dispatcher.CreateTimer();
        clock.Interval = TimeSpan.FromMilliseconds(250);
        clock.Tick += (_, _) => UpdatePosition();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        playback.Changed += Refresh;
        Refresh(this, EventArgs.Empty);
        clock.Start();
    }

    protected override void OnDisappearing()
    {
        clock.Stop();
        playback.Changed -= Refresh;
        if (artworkRequest is not null)
        {
            artworkRequest.Cancel();
            displayedTrack = null;
        }
        seeking = false;
        base.OnDisappearing();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 32 || ArtworkFrame is null) return;
        double side = Math.Round(Math.Min(280, (width - 32) * .74));
        if (ArtworkFrame.WidthRequest != side)
            ArtworkFrame.WidthRequest = ArtworkFrame.HeightRequest = side;
    }

    private void Refresh(object? sender, EventArgs e)
    {
        var track = playback.Current;
        if (!ReferenceEquals(displayedTrack, track) || TrackTitle.Text is null)
        {
            displayedTrack = track;
            artworkRequest?.Cancel();
            SetArtwork(track?.ArtworkPath);
            if (track?.ContentUri is not null) _ = LoadPlaybackArtworkAsync(track);
            TrackTitle.Text = track?.Title ?? "Sin reproducción";
            Credits.Text = track?.Credits;
            Format.Text = track?.Format;
            AudioInfo.IsVisible = track is not null;
            TechnicalInfo.Text = DescribeAudio(track?.Row.Song.Metadata);
        }
        bool queueChanged = !ReferenceEquals(displayedQueue, playback.Queue);
        if (queueChanged)
        {
            displayedQueue = playback.Queue;
            queueRows = displayedQueue.Select(item => new PlaybackQueueRow(item)).ToArray();
            QueueList.ItemsSource = queueRows;
            QueueSummary.Text = queueRows.Length switch
            {
                0 => "",
                1 => "1 canción",
                _ => $"{queueRows.Length:N0} canciones"
            };
        }

        if (queueChanged || !ReferenceEquals(activeQueueRow?.Track, track))
        {
            activeQueueRow?.IsCurrent = false;
            activeQueueRow = Array.Find(queueRows, item => ReferenceEquals(item.Track, track));
            activeQueueRow?.IsCurrent = true;
        }

        EmptyQueue.IsVisible = playback.Queue.Count == 0;
        Transport.IsEnabled = track is not null;
        PlaybackIndicator.IsVisible = playback.IsPlaying;
        PlaybackHeading.Text = playback.IsPlaying ? "Reproducción activa" : "Reproducción";
        bool playing = playback.IsPlaying || playback.IsPreparing;
        PlayButton.Source = playing ? "transport_pause.png" : "transport_play.png";
        SemanticProperties.SetDescription(PlayButton, playing ? "Pausar" : "Reproducir");
        ShuffleButton.BackgroundColor = Color.FromArgb(playback.Shuffle ? "#14564A" : "#0B1D22");
        SemanticProperties.SetDescription(ShuffleButton, playback.Shuffle ? "Modo aleatorio activado" : "Modo aleatorio desactivado");
        RepeatButton.Source = playback.Repeat switch { 1 => "repeat.png", 2 => "repeat_one.png", _ => "repeat_off.png" };
        RepeatButton.BackgroundColor = Color.FromArgb(playback.Repeat > 0 ? "#14564A" : "#0B1D22");
        SemanticProperties.SetDescription(RepeatButton, playback.Repeat switch
        {
            1 => "Repetir cola", 2 => "Repetir canción", _ => "Repetición desactivada"
        });
        Status.Text = playback.Error ?? (playback.IsPreparing ? "Cargando canción…" : null);
        Status.IsVisible = Status.Text is not null;
        UpdatePosition();
    }

    private static string DescribeAudio(TrackMetadata? metadata)
    {
        List<string> details = [];
        if (metadata?.AudioSampleRate is > 0 and var rate) details.Add($"{rate / 1000d:0.#} kHz");
        if (metadata?.AudioBitrate is > 0 and var bitrate) details.Add($"{bitrate} kbps");
        return string.Join(" · ", details);
    }

    private async Task LoadPlaybackArtworkAsync(LibraryTrack track)
    {
        var request = new CancellationTokenSource();
        artworkRequest = request;
        var token = request.Token;
        try
        {
            var path = await Task.Run(() => CoverThumbnail.GetPlaybackArtworkAsync(track, metadata, token), token);
            if (!token.IsCancellationRequested && ReferenceEquals(track, displayedTrack) && path is not null)
                SetArtwork(path);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(artworkRequest, request)) artworkRequest = null;
            request.Dispose();
        }
    }

    private void SetArtwork(string? path)
    {
        Artwork.Margin = path is null ? new Thickness(42) : Thickness.Zero;
        Artwork.Source = path ?? "tonic_desktop.png";
    }

    private void UpdatePosition()
    {
        if (seeking) return;
        updatingPosition = true;
        Position.IsEnabled = playback.Duration > 0;
        Position.Maximum = Math.Max(1, playback.Duration);
        Position.Value = Math.Clamp(playback.Position, 0, Position.Maximum);
        Duration.Text = FormatTime(playback.Duration);
        Elapsed.Text = FormatTime(playback.Position);
        updatingPosition = false;
    }

    private static string FormatTime(double milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private void SelectTrack(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaybackQueueRow item) return;
        QueueList.SelectedItem = null;
        playback.PlayQueued(item.Track);
    }

    private void StartSeek(object? sender, EventArgs e) => seeking = true;
    private void CompleteSeek(object? sender, EventArgs e)
    {
        seeking = false;
        playback.Seek((int)Position.Value);
    }

    private void PositionChanged(object? sender, ValueChangedEventArgs e)
    {
        if (updatingPosition || playback is null) return;
        if (seeking) Elapsed.Text = FormatTime(e.NewValue);
        else if (Position.IsEnabled) playback.Seek((int)e.NewValue);
    }

    private void Toggle(object? sender, EventArgs e) => playback.Toggle();
    private void Previous(object? sender, EventArgs e) => playback.Previous();
    private void Next(object? sender, EventArgs e) => playback.Next();
    private void Shuffle(object? sender, EventArgs e) => playback.ToggleShuffle();
    private void Repeat(object? sender, EventArgs e) => playback.CycleRepeat();
    private void ReturnToLibrary(object? sender, EventArgs e) => LibraryRequested?.Invoke(this, e);
    private async void ShowLyricsPlaceholder(object? sender, EventArgs e) =>
        await DisplayAlertAsync("Letras", "Trabajando en ello", "Aceptar");
}
