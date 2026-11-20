using MusicLayer.Application.UseCases.Lyrics;
using Xunit;

namespace MusicLayer.Application.Tests;

public sealed class LrcParserTests
{
    [Theory]
    [InlineData("[00:02]", 2000)]
    [InlineData("[0:02.1]", 2100)]
    [InlineData("[00:02.12]", 2120)]
    [InlineData("[00:02.123]", 2123)]
    [InlineData("[00:02:12]", 2120)]
    [InlineData("[64:02.001]", 3842001)]
    public void ParsesTimestampsAndFractions(string timestamp, int milliseconds)
    {
        var line = Assert.Single(LrcParser.Parse(timestamp + " Texto de prueba"));
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), line.Start);
        Assert.Equal("Texto de prueba", line.Text);
    }

    [Fact]
    public void OrdersRepeatedTimestampsAndIgnoresMetadataAndMalformedLines()
    {
        var lines = LrcParser.Parse("\uFEFF[ar:Lía]\r\n[00:10.5][00:01] Repetida\r\n" +
            "[51:60.00] Inválida\n[00:02.1234] Inválida\n[00:02 Falta cierre\nTexto suelto\n[00:05] [Estribillo]");

        Assert.Collection(lines,
            line => Assert.Equal(new TimedLyricLine(TimeSpan.FromSeconds(1), "Repetida"), line),
            line => Assert.Equal(new TimedLyricLine(TimeSpan.FromSeconds(5), "[Estribillo]"), line),
            line => Assert.Equal(new TimedLyricLine(TimeSpan.FromSeconds(10.5), "Repetida"), line));
    }

    [Theory]
    [InlineData("+250", -150)]
    [InlineData("-250", 350)]
    public void OffsetAppliesToAllLinesEvenWhenItAppearsLast(string offset, int milliseconds)
    {
        var line = Assert.Single(LrcParser.Parse($"[00:00.100] Texto\n[OFFSET: {offset}]\n[offset:inválido]"));
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), line.Start);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[ar:Artista]\n[00:80.00] Inválida\n[999999999999:00] Inválida")]
    public void EmptyOrInvalidInputHasNoTimedLines(string? lyrics)
        => Assert.Empty(LrcParser.Parse(lyrics));
}
