using MusicLayer.Presentation.Maui.Platforms.Android;

namespace MusicLayer.Presentation.Maui;

public partial class MiniPlayer : ContentView
{
    private AndroidPlayback? playback;
    private LibraryTrack? displayedTrack;
    private readonly IDispatcherTimer clock;
    public event EventHandler? OpenRequested;

    public MiniPlayer()
    {
        InitializeComponent();
        clock = Dispatcher.CreateTimer();
        clock.Interval = TimeSpan.FromMilliseconds(500);
        clock.Tick += (_, _) => UpdateProgress();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) =>
        {
            clock.Stop();
            if (playback is not null) playback.Changed -= Refresh;
        };
    }

    internal void Connect(AndroidPlayback player)
    {
        playback = player;
        if (IsLoaded) Subscribe();
    }

    private void Subscribe()
    {
        if (playback is null) return;
        playback.Changed -= Refresh;
        playback.Changed += Refresh;
        Refresh(this, EventArgs.Empty);
    }

    private void Refresh(object? sender, EventArgs e)
    {
        var track = playback?.Current;
        IsVisible = track is not null;
        if (!ReferenceEquals(track, displayedTrack))
        {
            displayedTrack = track;
            Cover.Source = track?.ArtworkPath ?? "sixteenth_notes.png";
            TrackTitle.Text = track?.Title;
            Credits.Text = track?.Credits;
        }
        bool playing = playback?.IsPlaying == true || playback?.IsPreparing == true;
        ToggleButton.Source = playing ? "transport_pause.png" : "transport_play.png";
        SemanticProperties.SetDescription(ToggleButton, playing ? "Pausar" : "Reproducir");
        UpdateProgress();
        if (playback?.IsPlaying == true)
        {
            if (!clock.IsRunning) clock.Start();
        }
        else clock.Stop();
    }

    private void UpdateProgress() =>
        Progress.Progress = playback is { Duration: > 0 } player
            ? Math.Clamp((double)player.Position / player.Duration, 0, 1) : 0;

    private void Open(object? sender, EventArgs e) => OpenRequested?.Invoke(this, e);
    private void Toggle(object? sender, EventArgs e) => playback?.Toggle();
}
