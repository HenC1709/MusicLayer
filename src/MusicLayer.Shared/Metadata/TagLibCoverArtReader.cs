using MusicLayer.Application.Abstractions;
using TagLib;

namespace MusicLayer.Shared.Metadata;

public sealed class TagLibCoverArtReader : ICoverArtReader
{
    public byte[]? ReadEmbeddedCover(string filePath)
    {
        if (Path.GetExtension(filePath).ToLowerInvariant() is not (".flac" or ".mp3" or ".wav"))
            return null;

        using var file = TagLib.File.Create(filePath, ReadStyle.None);
        return ReadPicture(file);
    }

    internal static byte[]? ReadPicture(TagLib.File file)
    {
        var pictures = file.Tag.Pictures.Where(p => p.MimeType != "-->" && p.Data.Count > 0);
        var cover = pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? pictures.FirstOrDefault();
        return cover?.Data.Data;
    }
}
