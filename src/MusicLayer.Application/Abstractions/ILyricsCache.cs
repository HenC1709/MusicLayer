using MusicLayer.Application.UseCases.Lyrics;

namespace MusicLayer.Application.Abstractions;

public interface ILyricsCache
{
    Task<LyricsRecord?> ReadAsync(LyricsCacheKey key, CancellationToken cancellationToken);
    Task WriteAsync(LyricsCacheKey key, LyricsRecord record, CancellationToken cancellationToken);
}
