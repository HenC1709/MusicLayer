using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class LyricsMatcherTests
{
    private static readonly TrackMetadata Track = new("Café de noche", "Lía", "Azul");
    private static readonly LyricsRecord Studio = new(1, "Cafe\u0301 de  noche", "LÍA", "Azul", 180, false, "Línea", null);

    [Theory]
    [InlineData(178.0, true)]
    [InlineData(182.0, true)]
    [InlineData(182.1, false)]
    [InlineData(null, false)]
    public void DirectMatchChecksTheDurationAgainstTheActualTrack(double? seconds, bool expected)
    {
        var matcher = new LyricsMatcher(Track, TimeSpan.FromMinutes(3));
        Assert.Equal(expected, matcher.AcceptDirectMatch(Studio with { Duration = seconds }));
    }

    [Fact]
    public void SearchRejectsOtherVersionsAndChoosesAnExplicitAlbumMatch()
    {
        var matcher = new LyricsMatcher(Track, TimeSpan.FromMinutes(3));
        LyricsRecord?[] candidates =
        [
            Studio with { Id = 2, TrackName = "Café de noche (Live)" },
            Studio with { Id = 3, ArtistName = "Otra artista" },
            Studio with { Id = 4, AlbumName = "Otra edición" },
            Studio with { Id = 5, AlbumName = null },
            Studio
        ];

        Assert.Same(Studio, matcher.ChooseMatch(candidates));
        Assert.Null(matcher.ChooseMatch(candidates[..3]));
    }

    [Fact]
    public void DistinctRecordsWithEqualEvidenceAreAmbiguous()
    {
        var matcher = new LyricsMatcher(Track with { Album = null }, null);
        Assert.False(matcher.AcceptDirectMatch(Studio));
        Assert.Null(matcher.ChooseMatch([Studio, Studio with { Id = 2, AlbumName = "En directo" }]));
        Assert.Same(Studio, matcher.ChooseMatch([Studio, Studio]));
    }
}
