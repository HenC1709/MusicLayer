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
            try { return Read(filePath, ReadStyle.Average); }
            catch (CorruptFileException)
            {
                // A damaged audio header can still have usable text tags.
                return Read(filePath, ReadStyle.None);
            }
        }
        catch (Exception error) when (error is CorruptFileException or UnsupportedFormatException
            or IOException or UnauthorizedAccessException)
        {
            // Metadata is optional. Let playback handle any unreadable audio separately.
            return null;
        }
    }

    private static TrackMetadata? Read(string filePath, ReadStyle style)
    {
        using var file = TagLib.File.Create(filePath, style);
        var title = Normalize(file.Tag.Title);
        var artist = Normalize(file.Tag.JoinedPerformers);
        var album = Normalize(file.Tag.Album);
        var genre = Normalize(file.Tag.FirstGenre);
        var audio = file.Properties;
        if (title is null && artist is null && album is null && genre is null)
            return null;
        return new TrackMetadata(title, artist, album)
        {
            Genre = genre,
            Duration = audio?.Duration is { Ticks: > 0 } duration ? duration : null,
            AudioBitrate = audio?.AudioBitrate is > 0 ? audio.AudioBitrate : null,
            AudioSampleRate = audio?.AudioSampleRate is > 0 ? audio.AudioSampleRate : null
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
