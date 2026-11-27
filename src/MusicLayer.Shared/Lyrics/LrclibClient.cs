using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Shared.Lyrics;

public sealed class LrclibClient(HttpClient http, TimeProvider? timeProvider = null) : ILyricsProvider, IDisposable
{
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(250);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextRequestAt;
    private DateTimeOffset retryAt;

    public async Task<LyricsLookupResult> LookupAsync(
        TrackMetadata metadata, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Artist))
            return new(LyricsLookupStatus.NotFound);

        var matcher = new LyricsMatcher(metadata, duration);
        var signature = $"track_name={Uri.EscapeDataString(metadata.Title.Trim())}&artist_name={Uri.EscapeDataString(metadata.Artist.Trim())}";
        if (!string.IsNullOrWhiteSpace(metadata.Album))
            signature += $"&album_name={Uri.EscapeDataString(metadata.Album.Trim())}";
        var lookup = signature;
        if (duration is { TotalSeconds: >= 1 and <= 3600 } length)
            lookup += "&duration=" + length.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (retryAt > time.GetUtcNow())
                return Unavailable();

            var direct = await GetAsync($"api/get?{lookup}", LyricsJsonContext.Default.LyricsRecord, cancellationToken)
                .ConfigureAwait(false);
            if (direct is not null && matcher.AcceptDirectMatch(direct))
                return ToResult(direct);

            var candidates = await GetAsync($"api/search?{signature}", LyricsJsonContext.Default.LyricsRecordArray, cancellationToken)
                .ConfigureAwait(false);
            return ToResult(candidates is null ? null : matcher.ChooseMatch(candidates));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException
            || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable();
        }
        finally { gate.Release(); }
    }

    private async Task<T?> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken) where T : class
    {
        var delay = nextRequestAt - time.GetUtcNow();
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                SetRetryAfter(response);

            cancellationToken.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
                mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) != true)
                throw new JsonException("LRCLIB did not return JSON.");

            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("LRCLIB returned a null payload.");
        }
        finally { nextRequestAt = time.GetUtcNow() + RequestSpacing; }
    }

    private void SetRetryAfter(HttpResponseMessage response)
    {
        var now = time.GetUtcNow();
        var retry = response.Headers.RetryAfter;
        var fallback = TimeSpan.FromSeconds(response.StatusCode == HttpStatusCode.TooManyRequests ? 30 : 1);
        var delay = retry?.Delta ?? (retry?.Date is { } date ? date - now : fallback);
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        var jitter = TimeSpan.FromSeconds(Math.Min(1, delay.TotalSeconds * 0.3) * Random.Shared.NextDouble());
        retryAt = delay >= DateTimeOffset.MaxValue - now - jitter ? DateTimeOffset.MaxValue : now + delay + jitter;
    }

    private LyricsLookupResult Unavailable()
        => new(LyricsLookupStatus.TemporarilyUnavailable, RetryAfter: retryAt > time.GetUtcNow() ? retryAt : null);

    private static LyricsLookupResult ToResult(LyricsRecord? record)
    {
        if (record is { Instrumental: true })
            return new(LyricsLookupStatus.Instrumental, record);
        if (record is not null && (!string.IsNullOrWhiteSpace(record.SyncedLyrics) || !string.IsNullOrWhiteSpace(record.PlainLyrics)))
            return new(LyricsLookupStatus.Found, record);
        return new(LyricsLookupStatus.NotFound);
    }

    public void Dispose() => gate.Dispose();
}
