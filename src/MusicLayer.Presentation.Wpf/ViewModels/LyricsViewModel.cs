using System.ComponentModel;
using System.Text.RegularExpressions;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public enum LyricEmphasis { Distant, Nearby, Active }

public sealed class LyricRow(TimedLyricLine line) : INotifyPropertyChanged
{
    private readonly int[] wordEnds = Regex.Matches(line.Text, @"\S+")
        .Select(word => word.Index + word.Length).ToArray();

    public string Text => line.Text;
    public TimeSpan Start => line.Start;
    public bool IsActive => Emphasis == LyricEmphasis.Active;
    public int LitTextLength { get; private set; }
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

    internal void SetWordProgress(double? fraction)
    {
        var length = 0;
        if (fraction is { } progress && wordEnds.Length > 0)
        {
            var wordIndex = Math.Min((int)(progress * wordEnds.Length), wordEnds.Length - 1);
            length = wordEnds[wordIndex];
        }

        if (LitTextLength == length) return;
        LitTextLength = length;
        PropertyChanged?.Invoke(this, new(nameof(LitTextLength)));
    }
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
    public bool CanRetry => IsOpen && !closed && request is null &&
        document?.Kind is LyricsDocumentKind.NotFound or LyricsDocumentKind.TemporarilyUnavailable;
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

    public Task RetryAsync()
    {
        if (!CanRetry) return Task.CompletedTask;
        requested = false;
        StartIfReady();
        return pending;
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
        request = new CancellationTokenSource();
        SetStatus("Buscando letra…");
        Notify(nameof(CanRetry));
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
            if (request == source && !closed)
            {
                document = LyricsDocument.FromLookup(new(LyricsLookupStatus.TemporarilyUnavailable));
                SetStatus("Letra temporalmente no disponible");
            }
        }
        finally
        {
            if (request == source)
            {
                request = null;
                Notify(nameof(CanRetry));
            }
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
        Notify(nameof(CanRetry));
        UpdateActiveLine();
        Rows = [];
        Notify(nameof(Rows));
        Notify(nameof(PlainText));
        Notify(nameof(HasLyrics));
        SetStatus(track is null ? "Reproduce una canción para ver su letra." : "Esperando la reproducción…");
    }

    private void UpdateActiveLine()
    {
        var clock = progress.GetValueOrDefault();
        int index = progress is not null && document is not null ? document.GetActiveLineIndex(clock.Elapsed) : -1;
        var previous = ActiveLineIndex;
        if (previous != index)
        {
            if (previous >= 0) Rows[previous].SetWordProgress(null);
            ActiveLineIndex = index;
            UpdateEmphasisAround(previous);
            UpdateEmphasisAround(index);
        }

        if (index >= 0)
        {
            var row = Rows[index];
            var end = index + 1 < Rows.Count && Rows[index + 1].Start < clock.Duration
                ? Rows[index + 1].Start
                : clock.Duration;
            var fraction = end > row.Start
                ? Math.Clamp((clock.Elapsed - row.Start) / (end - row.Start), 0, 1)
                : 0d;
            row.SetWordProgress(fraction);
        }

        if (previous != index) Notify(nameof(ActiveLineIndex));
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
