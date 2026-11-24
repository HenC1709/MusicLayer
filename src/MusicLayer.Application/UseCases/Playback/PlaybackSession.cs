using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class PlaybackSession(PlaySong playSong)
{
    private PlaybackQueue queue = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    // Read after awaiting an operation when the caller owns the session's commands.
    public int? CurrentIndex => queue.CurrentIndex;
    public bool HasNext => queue.NextIndex.HasValue;
    public bool HasPrevious => queue.PreviousIndex.HasValue;
    public bool HasRandomNext => RandomCandidateCount > 0;
    private int RandomCandidateCount => queue.Songs.Count - (queue.CurrentIndex.HasValue ? 1 : 0);

    public int? ChooseRandomNextIndex()
    {
        var count = RandomCandidateCount;
        if (count == 0) return null;

        // Map a uniform draw over the eligible tracks back to the queue positions.
        var index = Random.Shared.Next(count);
        return queue.CurrentIndex is int current && index >= current ? index + 1 : index;
    }

    public async Task ReplaceQueueAsync(IEnumerable<Song> songs, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            queue.Replace(songs);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Song?> PlayAtAsync(int index, CancellationToken cancellationToken = default)
        => PlayAsync(() => index, cancellationToken);

    public Task<Song?> PlayNextAsync(CancellationToken cancellationToken = default)
        => PlayAsync(() => queue.NextIndex, cancellationToken);

    public Task<Song?> PlayPreviousAsync(CancellationToken cancellationToken = default)
        => PlayAsync(() => queue.PreviousIndex, cancellationToken);

    public Task<Song?> PlayQueueAsync(IReadOnlyList<Song> songs, int index, CancellationToken cancellationToken = default)
        => PlayAsync(() => index, cancellationToken, songs);

    private async Task<Song?> PlayAsync(Func<int?> getIndex, CancellationToken cancellationToken,
        IReadOnlyList<Song>? replacement = null)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (getIndex() is not int index)
                return null;

            var target = queue;
            if (replacement is not null)
            {
                target = new PlaybackQueue();
                target.Replace(replacement);
            }
            var song = target.GetSongAt(index);
            await playSong.ExecuteAsync(song, cancellationToken).ConfigureAwait(false);
            target.Select(index);
            queue = target;
            return song;
        }
        finally
        {
            gate.Release();
        }
    }
}
