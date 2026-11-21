using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;
using TagLib;

namespace MusicLayer.Infrastructure.Metadata;

public sealed class TagLibTrackMetadataReader : ITrackMetadataReader
{
    public TrackMetadata? Read(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath, ReadStyle.None);
            var tag = file.Tag;
            var title = Normalize(tag.Title);
            var artist = Normalize(tag.JoinedPerformers);
            var album = Normalize(tag.Album);

            return title is null && artist is null && album is null
                ? null
                : new TrackMetadata(title, artist, album);
        }
        catch (Exception error) when (error is CorruptFileException or UnsupportedFormatException
            or IOException or UnauthorizedAccessException)
        {
            // Metadata is optional. Let playback handle any unreadable audio separately.
            return null;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
