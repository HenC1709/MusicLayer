using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Lyrics;

public sealed record LyricsCacheKey(string Title, string Artist, string? Album, long? DurationTicks)
{
    public static LyricsCacheKey? FromTrack(TrackMetadata metadata, TimeSpan? duration)
    {
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Artist))
            return null;

        return new LyricsCacheKey(
            LyricsMatcher.Normalize(metadata.Title).ToUpperInvariant(),
            LyricsMatcher.Normalize(metadata.Artist).ToUpperInvariant(),
            string.IsNullOrWhiteSpace(metadata.Album) ? null : LyricsMatcher.Normalize(metadata.Album).ToUpperInvariant(),
            duration is { Ticks: > 0 } length ? length.Ticks : null);
    }
}
