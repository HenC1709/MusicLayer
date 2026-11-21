namespace MusicLayer.Application.UseCases.Lyrics;

public enum LyricsDocumentKind { Synchronized, Plain, Instrumental, NotFound, TemporarilyUnavailable }

public sealed class LyricsDocument
{
    private LyricsDocument(LyricsDocumentKind kind, IReadOnlyList<TimedLyricLine>? lines = null, string? plainText = null,
        DateTimeOffset? retryAfter = null)
    {
        Kind = kind;
        Lines = lines ?? [];
        PlainText = plainText;
        RetryAfter = retryAfter;
    }

    public LyricsDocumentKind Kind { get; }
    public IReadOnlyList<TimedLyricLine> Lines { get; }
    public string? PlainText { get; }
    public DateTimeOffset? RetryAfter { get; }

    public static LyricsDocument FromLookup(LyricsLookupResult result)
    {
        if (result.Status == LyricsLookupStatus.TemporarilyUnavailable)
            return new(LyricsDocumentKind.TemporarilyUnavailable, retryAfter: result.RetryAfter);
        if (result.Status == LyricsLookupStatus.Instrumental)
            return new(LyricsDocumentKind.Instrumental);
        if (result.Status != LyricsLookupStatus.Found || result.Record is not { } record)
            return new(LyricsDocumentKind.NotFound);

        return FromLrc(record.SyncedLyrics, record.PlainLyrics);
    }

    internal static LyricsDocument FromLrc(string? syncedLyrics, string? plainLyrics = null)
    {
        var lines = LrcParser.Parse(syncedLyrics);
        if (lines.Any(line => line.Text.Length > 0))
            return new(LyricsDocumentKind.Synchronized, lines);
        if (!string.IsNullOrWhiteSpace(plainLyrics))
            return new(LyricsDocumentKind.Plain, plainText: plainLyrics);
        return new(LyricsDocumentKind.NotFound);
    }

    public int GetActiveLineIndex(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            return -1;

        // Find the last timestamp at or before position, even after seeking backwards.
        var first = 0;
        var end = Lines.Count;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (Lines[middle].Start <= position)
                first = middle + 1;
            else
                end = middle;
        }

        var index = first - 1;
        return index >= 0 && Lines[index].Text.Length > 0 ? index : -1;
    }
}
