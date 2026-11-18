using MusicLayer.Domain.Entities;
using Xunit;

namespace MusicLayer.Domain.Tests;

public sealed class PlaybackQueueTests
{
    [Fact]
    public void EmptyQueueHasNoSelectionAndCannotNavigate()
    {
        var queue = new PlaybackQueue();

        Assert.Empty(queue.Songs);
        Assert.Null(queue.CurrentIndex);
        Assert.Null(queue.CurrentSong);
        Assert.False(queue.MoveNext());
        Assert.False(queue.MovePrevious());
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Select(0));
    }

    [Fact]
    public void PreservesOrderAndMovesBetweenAdjacentPositions()
    {
        Song[] songs = [new("z.mp3"), new("a.wav"), new("m.flac")];
        var queue = new PlaybackQueue();
        queue.Replace(songs);

        Assert.Equal(songs, queue.Songs);
        Assert.Null(queue.CurrentSong);
        Assert.False(queue.MovePrevious());
        Assert.Null(queue.CurrentIndex);
        Assert.True(queue.MoveNext());
        Assert.Same(songs[0], queue.CurrentSong);

        queue.Select(2);
        Assert.Same(songs[2], queue.CurrentSong);
        Assert.True(queue.MovePrevious());
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(songs[1], queue.CurrentSong);
        Assert.True(queue.MoveNext());
        Assert.Same(songs[2], queue.CurrentSong);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RepeatedNavigationAtEitherEndKeepsCurrentPosition(int count)
    {
        var songs = Enumerable.Range(0, count).Select(i => new Song($"track-{i}.mp3")).ToArray();
        var queue = new PlaybackQueue();
        queue.Replace(songs);
        Assert.True(queue.MoveNext());
        Assert.False(queue.MovePrevious());
        Assert.False(queue.MovePrevious());
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Same(songs[0], queue.CurrentSong);

        queue.Select(count - 1);
        Assert.False(queue.MoveNext());
        Assert.False(queue.MoveNext());
        Assert.Equal(count - 1, queue.CurrentIndex);
        Assert.Same(songs[^1], queue.CurrentSong);
    }

    [Fact]
    public void DuplicateSongsOccupySeparatePositions()
    {
        var song = new Song("duplicate.mp3");
        var queue = new PlaybackQueue();
        queue.Replace([song, song]);

        Assert.True(queue.MoveNext());
        Assert.Equal(0, queue.CurrentIndex);
        Assert.True(queue.MoveNext());
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(song, queue.CurrentSong);
        Assert.True(queue.MovePrevious());
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void InvalidSelectionKeepsCurrentSong(int index)
    {
        var song = new Song("current.mp3");
        var queue = new PlaybackQueue();
        queue.Replace([new Song("first.mp3"), song]);
        queue.Select(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Select(index));
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(song, queue.CurrentSong);
    }

    [Fact]
    public void ReplacingSongsResetsSelectionAndEmptyInputClearsQueue()
    {
        var queue = new PlaybackQueue();
        queue.Replace([new Song("old.mp3")]);
        queue.Select(0);

        var replacement = new Song("new.mp3");
        queue.Replace([replacement]);
        Assert.Same(replacement, Assert.Single(queue.Songs));
        Assert.Null(queue.CurrentIndex);
        Assert.Null(queue.CurrentSong);
        Assert.True(queue.MoveNext());
        Assert.Same(replacement, queue.CurrentSong);

        queue.Replace([]);
        Assert.Empty(queue.Songs);
        Assert.Null(queue.CurrentIndex);
        Assert.Null(queue.CurrentSong);
        Assert.False(queue.MoveNext());
        Assert.False(queue.MovePrevious());
    }

    [Fact]
    public void InvalidReplacementKeepsSongsAndSelection()
    {
        var first = new Song("first.mp3");
        var second = new Song("second.mp3");
        var queue = new PlaybackQueue();
        queue.Replace([first, second]);
        queue.Select(1);

        Assert.Throws<ArgumentNullException>(() => queue.Replace(null!));
        Assert.Throws<ArgumentException>(() => queue.Replace([new Song("new.mp3"), null!]));
        Assert.Collection(queue.Songs, song => Assert.Same(first, song), song => Assert.Same(second, song));
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(second, queue.CurrentSong);
    }

    [Fact]
    public void CopiesInputAndExposesReadOnlySongs()
    {
        var original = new Song("original.mp3");
        Song[] input = [original];
        var queue = new PlaybackQueue();
        queue.Replace(input);

        input[0] = new Song("changed.mp3");
        Assert.Same(original, Assert.Single(queue.Songs));
        if (queue.Songs is IList<Song> list)
            Assert.Throws<NotSupportedException>(() => list[0] = input[0]);
    }
}
