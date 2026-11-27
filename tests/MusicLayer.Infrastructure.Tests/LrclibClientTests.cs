using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;
using MusicLayer.Shared.Lyrics;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class LrclibClientTests
{
    private static readonly TrackMetadata Track = new("Café & sol", "Lía + Luz", "Álbum #1");
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(180.5);
    private static readonly LyricsRecord Candidate = new(42, Track.Title, Track.Artist, Track.Album,
        180, false, "Una línea de prueba", "[00:12.00]Una línea de prueba");

    [Fact]
    public async Task EncodesTheSignatureAndReturnsLyricsFromTheDirectLookup()
    {
        using var http = CreateHttp((request, _, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/get?track_name=Caf%C3%A9%20%26%20sol&artist_name=L%C3%ADa%20%2B%20Luz&album_name=%C3%81lbum%20%231&duration=180.5",
                request.RequestUri?.PathAndQuery);
            Assert.Contains("MusicLayer/1.0.0", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json(Candidate));
        });
        using var client = new LrclibClient(http);
        var result = await client.LookupAsync(Track, Duration);
        Assert.Equal(LyricsLookupStatus.Found, result.Status);
        Assert.Equal(Candidate, result.Record);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesAtMostOneSearchAfterNotFoundOrARejectedMatch(bool rejectedDirectMatch)
    {
        var count = 0;
        using var http = CreateHttp((request, index, _) =>
        {
            count++;
            if (index == 1)
                return Task.FromResult(rejectedDirectMatch
                    ? Json(Candidate with { Duration = 190 })
                    : new HttpResponseMessage(HttpStatusCode.NotFound));

            Assert.Equal(2, index);
            Assert.StartsWith("/api/search?", request.RequestUri?.PathAndQuery);
            Assert.DoesNotContain("duration=", request.RequestUri?.Query);
            return Task.FromResult(Json(new[] { Candidate with { AlbumName = "Otro álbum" }, Candidate }));
        });
        using var client = new LrclibClient(http);
        Assert.Equal(Candidate, (await client.LookupAsync(Track, Duration)).Record);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(true, null, LyricsLookupStatus.Instrumental)]
    [InlineData(false, null, LyricsLookupStatus.NotFound)]
    [InlineData(false, "Texto simple", LyricsLookupStatus.Found)]
    public async Task DistinguishesInstrumentalEmptyAndPlainResults(bool instrumental, string? plain, LyricsLookupStatus status)
    {
        using var http = CreateHttp((_, index, _) =>
        {
            Assert.Equal(1, index);
            return Task.FromResult(Json(Candidate with { Instrumental = instrumental, PlainLyrics = plain, SyncedLyrics = null }));
        });
        using var client = new LrclibClient(http);
        Assert.Equal(status, (await client.LookupAsync(Track, null)).Status);
    }

    [Fact]
    public async Task MissingTagsSkipHttpAndAnEmptySearchIsNotFound()
    {
        var count = 0;
        using var http = CreateHttp((_, index, _) =>
        {
            count++;
            return Task.FromResult(index == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(Array.Empty<LyricsRecord>()));
        });
        using var client = new LrclibClient(http);
        Assert.Equal(LyricsLookupStatus.NotFound, (await client.LookupAsync(Track with { Artist = null }, null)).Status);
        Assert.Equal(0, count);
        Assert.Equal(LyricsLookupStatus.NotFound, (await client.LookupAsync(Track, null)).Status);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task RetryAfterAppliesAcrossTracksAndAllowsRequestsAfterTheDeadline(HttpStatusCode status, bool absolute)
    {
        var clock = new TestClock();
        var requests = 0;
        using var http = CreateHttp((_, index, _) =>
        {
            requests++;
            if (index > 1)
                return Task.FromResult(Json(Candidate));
            var response = new HttpResponseMessage(status) { Content = new StringContent("Please wait") };
            var wait = TimeSpan.FromSeconds(7);
            response.Headers.RetryAfter = absolute ? new(clock.GetUtcNow() + wait) : new(wait);
            return Task.FromResult(response);
        });
        using var client = new LrclibClient(http, clock);
        var result = await client.LookupAsync(Track, Duration);
        Assert.Equal(LyricsLookupStatus.TemporarilyUnavailable, result.Status);
        Assert.InRange(result.RetryAfter!.Value, clock.GetUtcNow().AddSeconds(7), clock.GetUtcNow().AddSeconds(8));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(result, await client.LookupAsync(Track with { Title = "Siguiente" }, Duration));
        Assert.Equal(1, requests);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(LyricsLookupStatus.Found, (await client.LookupAsync(Track, Duration)).Status);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("json")]
    [InlineData("html")]
    [InlineData("server")]
    [InlineData("timeout")]
    public async Task TemporaryFailuresDoNotTriggerTheSearchFallback(string failure)
    {
        using var http = CreateHttp(async (_, index, token) =>
        {
            Assert.Equal(1, index);
            return failure switch
            {
                "network" => throw new HttpRequestException("Red no disponible"),
                "json" => new HttpResponseMessage { Content = new StringContent("{", new MediaTypeHeaderValue("application/json")) },
                "html" => new HttpResponseMessage { Content = new StringContent("<html>", new MediaTypeHeaderValue("text/html")) },
                "server" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                _ => await WaitForCancellation(token)
            };
        });
        http.Timeout = TimeSpan.FromMilliseconds(100);
        using var client = new LrclibClient(http);
        Assert.Equal(LyricsLookupStatus.TemporarilyUnavailable, (await client.LookupAsync(Track, Duration)).Status);
    }

    [Fact]
    public async Task CancellingAnObsoleteRequestLetsTheNextTrackProceed()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = CreateHttp(async (_, index, token) =>
        {
            if (index == 1)
            {
                started.SetResult();
                return await WaitForCancellation(token);
            }
            Assert.Equal(2, index);
            return Json(Candidate);
        });
        using var client = new LrclibClient(http);
        using var obsolete = new CancellationTokenSource();
        var first = client.LookupAsync(Track with { Title = "Anterior" }, Duration, obsolete.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var next = client.LookupAsync(Track, Duration);
        Assert.False(next.IsCompleted);
        await obsolete.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(Candidate, (await next.WaitAsync(TimeSpan.FromSeconds(2))).Record);
    }

    private static async Task<HttpResponseMessage> WaitForCancellation(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException();
    }

    private static HttpResponseMessage Json<T>(T payload) => new() { Content = JsonContent.Create(payload) };

    private static HttpClient CreateHttp(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var http = new HttpClient(new Handler(send)) { BaseAddress = new Uri("https://lrclib.net") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicLayer/1.0.0 (https://github.com/HenC1709/MusicLayerr)");
        return http;
    }

    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, Interlocked.Increment(ref requests), cancellationToken);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
