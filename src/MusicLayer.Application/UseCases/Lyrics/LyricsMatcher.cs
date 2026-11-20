using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Lyrics;

public sealed class LyricsMatcher(TrackMetadata metadata, TimeSpan? duration)
{
    public bool AcceptDirectMatch(LyricsRecord candidate) => Score(candidate) > 0;

    public LyricsRecord? ChooseMatch(IEnumerable<LyricsRecord?> candidates)
    {
        LyricsRecord? best = null;
        var bestScore = -1;
        var ambiguous = false;
        foreach (var candidate in candidates)
        {
            if (candidate is null) continue;
            var score = Score(candidate);
            if (score < 0) continue;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
                ambiguous = false;
            }
            else if (score == bestScore && candidate.Id != best?.Id)
            {
                ambiguous = true;
            }
        }
        return ambiguous ? null : best;
    }

    private int Score(LyricsRecord candidate)
    {
        if (candidate.Id <= 0 || !EqualTag(metadata.Title, candidate.TrackName) ||
            !EqualTag(metadata.Artist, candidate.ArtistName))
            return -1;

        var bothAlbumsKnown = !string.IsNullOrWhiteSpace(metadata.Album) && !string.IsNullOrWhiteSpace(candidate.AlbumName);
        if (bothAlbumsKnown && !EqualTag(metadata.Album, candidate.AlbumName))
            return -1;

        var durationScore = 0;
        if (duration is { TotalSeconds: > 0 } length)
        {
            if (candidate.Duration is not > 0 || Math.Abs(length.TotalSeconds - candidate.Duration.Value) > 2)
                return -1;
            durationScore = 1;
        }

        // A shared title and artist alone do not confirm a direct lookup.
        // Competing search results with the same evidence stay ambiguous.
        return (bothAlbumsKnown ? 2 : 0) + durationScore;
    }

    private static bool EqualTag(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
            string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Normalize();
}
