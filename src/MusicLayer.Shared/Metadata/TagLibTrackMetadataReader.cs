using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;
using TagLib;

namespace MusicLayer.Shared.Metadata;

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
        return ReadTags(file);
    }

    public (TrackMetadata? Metadata, byte[]? Cover) ReadSelection(string fileName, Stream stream)
    {
        var input = new StreamFileAbstraction(fileName, stream);
        TagLib.File file;
        try { file = TagLib.File.Create(input, ReadStyle.Average); }
        catch (CorruptFileException) { file = TagLib.File.Create(input, ReadStyle.None); }

        using (file)
            return (ReadTags(file), TagLibCoverArtReader.ReadPicture(file));
    }

    private static TrackMetadata? ReadTags(TagLib.File file)
    {
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

    private sealed class StreamFileAbstraction(string name, Stream stream) : TagLib.File.IFileAbstraction
    {
        public string Name { get; } = name;
        public Stream ReadStream
        {
            get
            {
                stream.Position = 0;
                return stream;
            }
        }
        public Stream WriteStream => throw new NotSupportedException();

        // TagLib can reopen the stream. The importer owns it and disposes it after reading.
        public void CloseStream(Stream stream) { }
    }
}
