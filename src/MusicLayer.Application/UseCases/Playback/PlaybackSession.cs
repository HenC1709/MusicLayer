using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Playback;

public sealed class PlaybackSession(PlaySong playSong)
{
    private readonly PlaybackQueue queue = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    // Read after awaiting an operation when the caller owns the session's commands.
    public int? CurrentIndex => queue.CurrentIndex;
    public bool HasNext => queue.NextIndex.HasValue;
    public bool HasPrevious => queue.PreviousIndex.HasValue;

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

    private async Task<Song?> PlayAsync(Func<int?> getIndex, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (getIndex() is not int index)
                return null;

            var song = queue.GetSongAt(index);
            await playSong.ExecuteAsync(song, cancellationToken).ConfigureAwait(false);
            queue.Select(index);
            return song;
        }
        finally
        {
            gate.Release();
        }
    }
}
