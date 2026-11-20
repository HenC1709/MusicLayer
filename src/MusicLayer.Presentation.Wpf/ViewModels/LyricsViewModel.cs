using System.ComponentModel;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public sealed class LyricRow(string text) : INotifyPropertyChanged
{
    public string Text { get; } = text;
    public bool IsActive
    {
        get;
        internal set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(IsActive)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LyricsViewModel(ILyricsProvider provider) : INotifyPropertyChanged
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
        if (track.Metadata is not { } tags || string.IsNullOrWhiteSpace(tags.Artist) || string.IsNullOrWhiteSpace(tags.Title))
        {
            SetStatus("Letra no disponible");
            return;
        }

        SetStatus("Buscando letra…");
        request = new CancellationTokenSource();
        var lookup = LoadAsync(tags, progress?.Duration, request);
        pending = pending.IsCompleted ? lookup : Task.WhenAll(pending, lookup);
    }

    private async Task LoadAsync(TrackMetadata tags, TimeSpan? duration, CancellationTokenSource source)
    {
        try
        {
            var result = await provider.LookupAsync(tags, duration, source.Token);
            source.Token.ThrowIfCancellationRequested();
            var parsed = await Task.Run(() => LyricsDocument.FromLookup(result), source.Token);
            if (request != source || source.IsCancellationRequested) return;
            document = parsed;
            Rows = parsed.Lines.Select(line => new LyricRow(line.Text)).ToArray();
            Notify(nameof(Rows));
            Notify(nameof(PlainText));
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
        SetStatus(track is null ? "Reproduce una canción para ver su letra." : "Esperando la reproducción…");
    }

    private void UpdateActiveLine()
    {
        int index = progress is { } clock && document is not null ? document.GetActiveLineIndex(clock.Elapsed) : -1;
        if (ActiveLineIndex == index) return;
        if (ActiveLineIndex >= 0) Rows[ActiveLineIndex].IsActive = false;
        ActiveLineIndex = index;
        if (index >= 0) Rows[index].IsActive = true;
        Notify(nameof(ActiveLineIndex));
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
