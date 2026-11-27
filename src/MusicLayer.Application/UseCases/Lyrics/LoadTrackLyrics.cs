using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Lyrics;

public sealed class LoadTrackLyrics(ILocalLyricsReader local, ILyricsProvider provider)
{
    public async Task<LyricsDocument> ExecuteAsync(Song track, TimeSpan? duration, CancellationToken cancellationToken,
        string? localKey = null, bool refresh = false)
    {
        var text = await local.ReadAsync(localKey ?? track.FilePath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var document = await Task.Run(() => LyricsDocument.FromLrc(text, isLocal: true), cancellationToken)
                .ConfigureAwait(false);
            if (document.Kind == LyricsDocumentKind.Synchronized)
                return document;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (track.Metadata is not { } tags || string.IsNullOrWhiteSpace(tags.Artist) || string.IsNullOrWhiteSpace(tags.Title))
            return LyricsDocument.FromLookup(new(LyricsLookupStatus.NotFound));

        var result = await (refresh
            ? provider.RefreshAsync(tags, duration, cancellationToken)
            : provider.LookupAsync(tags, duration, cancellationToken)).ConfigureAwait(false);
        return await Task.Run(() => LyricsDocument.FromLookup(result), cancellationToken).ConfigureAwait(false);
    }
}
