using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Lyrics;

public sealed class CachedLyricsProvider(ILyricsProvider remote, ILyricsCache cache, TimeProvider? timeProvider = null)
    : ILyricsProvider, IDisposable
{
    private static readonly TimeSpan MissingEntryLifetime = TimeSpan.FromMinutes(10);
    private const int MissingEntryLimit = 128;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim lookupGate = new(1, 1);
    private readonly Dictionary<LyricsCacheKey, DateTimeOffset> missing = [];

    public async Task<LyricsLookupResult> LookupAsync(
        TrackMetadata metadata, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = LyricsCacheKey.FromTrack(metadata, duration);
        if (key is null)
            return new(LyricsLookupStatus.NotFound);

        await lookupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await cache.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (record is not null && new LyricsMatcher(metadata, duration).ChooseMatch([record]) is not null)
            {
                var cached = new LyricsLookupResult(record.Instrumental ? LyricsLookupStatus.Instrumental : LyricsLookupStatus.Found, record);
                if (HasUsableLyrics(cached))
                    return cached;
            }

            if (missing.TryGetValue(key, out var expiresAt) && expiresAt > time.GetUtcNow())
                return new(LyricsLookupStatus.NotFound);

            missing.Remove(key);
            var result = await remote.LookupAsync(metadata, duration, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Record is { } confirmed && HasUsableLyrics(result))
                await cache.WriteAsync(key, confirmed, cancellationToken).ConfigureAwait(false);
            else if (result.Status == LyricsLookupStatus.NotFound)
                RememberMissing(key);

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { lookupGate.Release(); }
    }

    private void RememberMissing(LyricsCacheKey key)
    {
        if (missing.Count == MissingEntryLimit)
            missing.Remove(missing.MinBy(entry => entry.Value).Key);

        missing[key] = time.GetUtcNow() + MissingEntryLifetime;
    }

    private static bool HasUsableLyrics(LyricsLookupResult result)
        => LyricsDocument.FromLookup(result).Kind is
            LyricsDocumentKind.Synchronized or LyricsDocumentKind.Plain or LyricsDocumentKind.Instrumental;

    public void Dispose() => lookupGate.Dispose();
}
