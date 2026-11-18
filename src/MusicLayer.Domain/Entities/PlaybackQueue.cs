namespace MusicLayer.Domain.Entities;

public sealed class PlaybackQueue
{
    public IReadOnlyList<Song> Songs { get; private set; } = Array.Empty<Song>();
    public int? CurrentIndex { get; private set; }
    public Song? CurrentSong => CurrentIndex is int index ? Songs[index] : null;
    public int? NextIndex
    {
        get
        {
            var index = (CurrentIndex ?? -1) + 1;
            return index < Songs.Count ? index : null;
        }
    }

    public int? PreviousIndex => CurrentIndex is int index && index > 0 ? index - 1 : null;

    public void Replace(IEnumerable<Song> songs)
    {
        ArgumentNullException.ThrowIfNull(songs);
        var replacement = songs.ToArray();
        foreach (var song in replacement)
        {
            if (song is null)
                throw new ArgumentException("The queue cannot contain null songs.", nameof(songs));
        }

        Songs = Array.AsReadOnly(replacement);
        CurrentIndex = null;
    }

    public void Select(int index)
    {
        GetSongAt(index);
        CurrentIndex = index;
    }

    public Song GetSongAt(int index)
    {
        if (index < 0 || index >= Songs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return Songs[index];
    }

    public bool MoveNext()
    {
        if (NextIndex is not int nextIndex)
            return false;

        CurrentIndex = nextIndex;
        return true;
    }

    public bool MovePrevious()
    {
        if (PreviousIndex is not int previousIndex)
            return false;

        CurrentIndex = previousIndex;
        return true;
    }
}
