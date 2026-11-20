namespace MusicLayer.Application.UseCases.Lyrics;

public enum LyricsLookupStatus { Found, Instrumental, NotFound, TemporarilyUnavailable }

public sealed record LyricsLookupResult(
    LyricsLookupStatus Status, LyricsRecord? Record = null, DateTimeOffset? RetryAfter = null);

public sealed record LyricsRecord(
    long Id, string? TrackName, string? ArtistName, string? AlbumName, double? Duration,
    bool Instrumental, string? PlainLyrics, string? SyncedLyrics);
