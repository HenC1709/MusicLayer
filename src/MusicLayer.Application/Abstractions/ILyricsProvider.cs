using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.Abstractions;

public interface ILyricsProvider
{
    Task<LyricsLookupResult> LookupAsync(TrackMetadata metadata, TimeSpan? duration, CancellationToken cancellationToken = default);
}
