using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;
using MusicLayer.Infrastructure.Lyrics;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class JsonLyricsCacheTests : IDisposable
{
    private static readonly TrackMetadata Track = new("Café de noche", "Lía", "Azul");
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(180);
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("MusicLayer-Lyrics-");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoversConfirmedLyricsOrInstrumentalStatusFromDiskInANewSession(bool instrumental)
    {
        var expected = Found(Track, Length, instrumental: instrumental);
        using (var online = new CachedLyricsProvider(new StubProvider((_, _, _) => Task.FromResult(expected)), new JsonLyricsCache(folder.FullName)))
            Assert.Equal(expected, await online.LookupAsync(Track, Length));

        var offlineRemote = new StubProvider((_, _, _) => Task.FromResult(new LyricsLookupResult(LyricsLookupStatus.TemporarilyUnavailable)));
        using var offline = new CachedLyricsProvider(offlineRemote, new JsonLyricsCache(folder.FullName));
        var equivalentTags = new TrackMetadata("Cafe\u0301  DE noche", " LÍA ", "AZUL");
        Assert.Equal(expected, await offline.LookupAsync(equivalentTags, Length));
        Assert.Equal(0, offlineRemote.Calls);
        Assert.Single(folder.GetFiles("v1-*.json"));
    }

    [Fact]
    public async Task DifferentAlbumsAndDurationsUseDifferentEntries()
    {
        var remote = new StubProvider((metadata, length, _) => Task.FromResult(Found(metadata, length)));
        using var provider = new CachedLyricsProvider(remote, new JsonLyricsCache(folder.FullName));
        var otherAlbum = Track with { Album = "En directo" };
        var otherDuration = Length.Add(TimeSpan.FromSeconds(1));
        await provider.LookupAsync(Track, Length);
        await provider.LookupAsync(otherAlbum, Length);
        await provider.LookupAsync(Track, otherDuration);
        await provider.LookupAsync(Track, null);

        Assert.Equal(4, remote.Calls);
        Assert.Equal(4, folder.GetFiles("v1-*.json").Length);
        Assert.Equal(Found(Track, Length), await provider.LookupAsync(Track, Length));
        Assert.Equal(4, remote.Calls);
    }

    [Fact]
    public async Task ConcurrentLookupsForTheSameTrackShareTheCachedResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<LyricsLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new StubProvider((_, _, _) =>
        {
            started.TrySetResult();
            return response.Task;
        });
        using var provider = new CachedLyricsProvider(remote, new JsonLyricsCache(folder.FullName));
        var first = provider.LookupAsync(Track, Length);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.LookupAsync(Track, Length);
        response.SetResult(Found(Track, Length));

        Assert.Equal(await first, await second);
        Assert.Equal(1, remote.Calls);
        Assert.Single(folder.GetFiles());
    }

    [Fact]
    public async Task CorruptEntryIsReplacedAndAWriteFailureStillReturnsTheLyrics()
    {
        var remote = new StubProvider((metadata, length, _) => Task.FromResult(Found(metadata, length)));
        using var provider = new CachedLyricsProvider(remote, new JsonLyricsCache(folder.FullName));
        await provider.LookupAsync(Track, Length);
        var cacheFile = Assert.Single(folder.GetFiles());
        await File.WriteAllTextAsync(cacheFile.FullName, "{json truncado");

        Assert.Equal(Found(Track, Length), await provider.LookupAsync(Track, Length));
        Assert.Equal(Found(Track, Length), await provider.LookupAsync(Track, Length));
        Assert.Equal(2, remote.Calls);
        Assert.Single(folder.GetFiles());

        using var unwritable = new CachedLyricsProvider(remote, new JsonLyricsCache(cacheFile.FullName));
        Assert.Equal(Found(Track, Length), await unwritable.LookupAsync(Track, Length));
        Assert.Equal(3, remote.Calls);
    }

    [Fact]
    public async Task MissingResultsExpireInMemoryAndTemporaryErrorsAreNeverCached()
    {
        var clock = new TestClock();
        var unavailable = new LyricsLookupResult(LyricsLookupStatus.TemporarilyUnavailable);
        var notFound = new LyricsLookupResult(LyricsLookupStatus.NotFound);
        var next = unavailable;
        var remote = new StubProvider((_, _, _) => Task.FromResult(next));
        using var provider = new CachedLyricsProvider(remote, new JsonLyricsCache(folder.FullName), clock);
        Assert.Equal(unavailable, await provider.LookupAsync(Track, Length));
        next = notFound;
        Assert.Equal(notFound, await provider.LookupAsync(Track, Length));
        Assert.Equal(notFound, await provider.LookupAsync(Track, Length));
        Assert.Equal(2, remote.Calls);
        Assert.Empty(folder.GetFiles());

        clock.Advance(TimeSpan.FromMinutes(10));
        next = Found(Track, Length);
        Assert.Equal(next, await provider.LookupAsync(Track, Length));
        Assert.Equal(3, remote.Calls);
    }

    [Fact]
    public async Task CancelledLookupLeavesNoDiskEntryEvenIfTheRemoteReturnsLyrics()
    {
        using var obsolete = new CancellationTokenSource();
        var remote = new StubProvider((metadata, length, _) =>
        {
            obsolete.Cancel();
            return Task.FromResult(Found(metadata, length));
        });
        using var provider = new CachedLyricsProvider(remote, new JsonLyricsCache(folder.FullName));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.LookupAsync(Track, Length, obsolete.Token));
        Assert.Empty(folder.GetFiles());
    }

    private static LyricsLookupResult Found(TrackMetadata metadata, TimeSpan? length, bool instrumental = false)
        => new(instrumental ? LyricsLookupStatus.Instrumental : LyricsLookupStatus.Found,
            new LyricsRecord(42, metadata.Title, metadata.Artist, metadata.Album, length?.TotalSeconds, instrumental,
                instrumental ? null : "Letra de prueba", null));

    private sealed class StubProvider(Func<TrackMetadata, TimeSpan?, CancellationToken, Task<LyricsLookupResult>> lookup) : ILyricsProvider
    {
        public int Calls { get; private set; }

        public Task<LyricsLookupResult> LookupAsync(TrackMetadata metadata, TimeSpan? duration, CancellationToken cancellationToken = default)
        {
            Calls++;
            return lookup(metadata, duration, cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    public void Dispose() => folder.Delete(recursive: true);
}
