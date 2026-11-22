using System.ComponentModel;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public enum LyricEmphasis { Distant, Nearby, Active }

public sealed class LyricRow(TimedLyricLine line) : INotifyPropertyChanged
{
    public string Text => line.Text;
    public TimeSpan Start => line.Start;
    public bool IsActive => Emphasis == LyricEmphasis.Active;
    public LyricEmphasis Emphasis
    {
        get;
        internal set
        {
            if (field == value) return;
            var wasActive = IsActive;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(Emphasis)));
            if (wasActive != IsActive) PropertyChanged?.Invoke(this, new(nameof(IsActive)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LyricsViewModel(LoadTrackLyrics loadLyrics, Func<TimeSpan, Task> seekTo) : INotifyPropertyChanged
{
    private CancellationTokenSource? request;
    private Task pending = Task.CompletedTask;
    private Song? track;
    private PlaybackProgress? progress;
    private LyricsDocument? document;
    private bool sampled;
    private bool requested;
    private bool closed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<LyricRow> Rows { get; private set; } = [];
    public string? PlainText => document?.PlainText;
    public bool HasLyrics => Rows.Count > 0 || PlainText is not null;
    public string Status { get; private set; } = "Reproduce una canción para ver su letra.";
    public int ActiveLineIndex { get; private set; } = -1;
    public bool IsOpen
    {
        get;
        set
        {
            if (field == value || closed) return;
            field = value;
            ResetDocument();
            StartIfReady();
        }
    }

    internal void ChangeTrack(Song? song)
    {
        track = song;
        progress = null;
        sampled = false;
        ResetDocument();
    }

    public Task SeekToLineAsync(LyricRow row)
    {
        if (closed || !IsOpen || progress is not { } clock || row.Text.Length == 0 ||
            row.Start >= clock.Duration || !Rows.Contains(row))
            return Task.CompletedTask;

        return seekTo(row.Start);
    }

    internal void ObserveProgress(PlaybackProgress? snapshot)
    {
        progress = snapshot;
        sampled = true;
        StartIfReady();
        UpdateActiveLine();
    }

    private void StartIfReady()
    {
        if (!IsOpen || !sampled || requested || closed || track is null) return;
        requested = true;
        SetStatus("Buscando letra…");
        request = new CancellationTokenSource();
        var lookup = LoadAsync(track, progress?.Duration, request);
        pending = pending.IsCompleted ? lookup : Task.WhenAll(pending, lookup);
    }

    private async Task LoadAsync(Song song, TimeSpan? duration, CancellationTokenSource source)
    {
        try
        {
            var parsed = await loadLyrics.ExecuteAsync(song, duration, source.Token);
            if (request != source || source.IsCancellationRequested) return;
            document = parsed;
            Rows = parsed.Lines.Select(line => new LyricRow(line)).ToArray();
            Notify(nameof(Rows));
            Notify(nameof(PlainText));
            Notify(nameof(HasLyrics));
            SetStatus(parsed.Kind switch
            {
                LyricsDocumentKind.Synchronized => "Letra sincronizada",
                LyricsDocumentKind.Plain => "Letra sin sincronización",
                LyricsDocumentKind.Instrumental => "Pista instrumental",
                LyricsDocumentKind.TemporarilyUnavailable => "Letra temporalmente no disponible",
                _ => "Letra no disponible"
            });
            UpdateActiveLine();
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception)
        {
            if (request == source && !closed) SetStatus("Letra temporalmente no disponible");
        }
        finally
        {
            if (request == source) request = null;
            source.Dispose();
        }
    }

    private void ResetDocument()
    {
        var previous = request;
        request = null;
        previous?.Cancel();
        requested = false;
        document = null;
        UpdateActiveLine();
        Rows = [];
        Notify(nameof(Rows));
        Notify(nameof(PlainText));
        Notify(nameof(HasLyrics));
        SetStatus(track is null ? "Reproduce una canción para ver su letra." : "Esperando la reproducción…");
    }

    private void UpdateActiveLine()
    {
        int index = progress is { } clock && document is not null ? document.GetActiveLineIndex(clock.Elapsed) : -1;
        if (ActiveLineIndex == index) return;
        var previous = ActiveLineIndex;
        ActiveLineIndex = index;
        UpdateEmphasisAround(previous);
        UpdateEmphasisAround(index);
        Notify(nameof(ActiveLineIndex));
    }

    private void UpdateEmphasisAround(int center)
    {
        if (center < 0) return;
        for (var i = Math.Max(0, center - 1); i <= Math.Min(Rows.Count - 1, center + 1); i++)
        {
            Rows[i].Emphasis = Math.Abs(i - ActiveLineIndex) switch
            {
                0 => LyricEmphasis.Active,
                1 when ActiveLineIndex >= 0 => LyricEmphasis.Nearby,
                _ => LyricEmphasis.Distant
            };
        }
    }

    private void SetStatus(string value)
    {
        Status = value;
        Notify(nameof(Status));
    }

    internal Task CloseAsync()
    {
        closed = true;
        request?.Cancel();
        return pending;
    }

    private void Notify(string property) => PropertyChanged?.Invoke(this, new(property));
}
