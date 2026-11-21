using MusicLayer.Application.UseCases.Lyrics;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class LyricsDocumentTests
{
    [Fact]
    public void HighlightsTheLatestLineAcrossForwardAndBackwardJumps()
    {
        var document = Parse("[00:03.200] Primera\n[00:05.5]\n[00:07] Última", "Texto completo");
        Assert.Equal(LyricsDocumentKind.Synchronized, document.Kind);
        Assert.Null(document.PlainText);

        Assert.Equal(-1, document.GetActiveLineIndex(TimeSpan.FromMilliseconds(3199)));
        Assert.Equal(0, document.GetActiveLineIndex(TimeSpan.FromMilliseconds(3200)));
        Assert.Equal(-1, document.GetActiveLineIndex(TimeSpan.FromSeconds(6)));
        Assert.Equal(2, document.GetActiveLineIndex(TimeSpan.FromSeconds(8)));
        Assert.Equal(0, document.GetActiveLineIndex(TimeSpan.FromSeconds(4)));
        Assert.Equal(-1, document.GetActiveLineIndex(TimeSpan.Zero));
    }

    [Fact]
    public void HandlesLinesShiftedBeforeStartAndKeepsDuplicateTimestampsInSourceOrder()
    {
        var document = Parse("[00:00.1] Primera\n[00:00.1] Segunda\n[offset:+200]");
        Assert.Equal("Primera", document.Lines[0].Text);
        Assert.Equal(1, document.GetActiveLineIndex(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[00:70.00] Inválida")]
    [InlineData("[00:00]\n[00:20]")]
    public void UsesPlainLyricsWhenThereIsNoSynchronizedText(string? synced)
    {
        var document = Parse(synced, "Primera\nSegunda");
        Assert.Equal(LyricsDocumentKind.Plain, document.Kind);
        Assert.Equal("Primera\nSegunda", document.PlainText);
        Assert.Empty(document.Lines);
        Assert.Equal(-1, document.GetActiveLineIndex(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void KeepsAvailabilityStatesAndDoesNotProduceLyricsForInvalidText()
    {
        var retryAfter = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(LyricsDocumentKind.TemporarilyUnavailable, LyricsDocument.FromLookup(
            new(LyricsLookupStatus.TemporarilyUnavailable)).Kind);
        Assert.Equal(retryAfter, LyricsDocument.FromLookup(
            new(LyricsLookupStatus.TemporarilyUnavailable, RetryAfter: retryAfter)).RetryAfter);
        Assert.Equal(LyricsDocumentKind.Instrumental, LyricsDocument.FromLookup(new(LyricsLookupStatus.Instrumental)).Kind);
        Assert.Equal(LyricsDocumentKind.NotFound, LyricsDocument.FromLookup(new(LyricsLookupStatus.NotFound)).Kind);
        Assert.Equal(LyricsDocumentKind.NotFound, Parse("[ti:Solo metadatos]").Kind);
    }

    private static LyricsDocument Parse(string? synced, string? plain = null)
        => LyricsDocument.FromLookup(new(LyricsLookupStatus.Found,
            new LyricsRecord(1, "Título", "Artista", null, null, false, plain, synced)));
}
