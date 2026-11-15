using MusicLayer.Domain.Entities;
using Xunit;

namespace MusicLayer.Domain.Tests;

public sealed class SongTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RejectsEmptyLocation(string? path)
        => Assert.ThrowsAny<ArgumentException>(() => new Song(path!));

    [Fact]
    public void NormalizesLocationAndUsesFileNameAsTitle()
    {
        var song = new Song(Path.Combine("music", "My Song.mp3"));
        Assert.True(Path.IsPathFullyQualified(song.FilePath));
        Assert.Equal("My Song", song.DisplayName);
    }
}
