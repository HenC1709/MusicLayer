using MusicLayer.Application.Abstractions;
using TagLib;

namespace MusicLayer.Infrastructure.Metadata;

public sealed class TagLibCoverArtReader : ICoverArtReader
{
    public byte[]? ReadEmbeddedCover(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".flac", StringComparison.OrdinalIgnoreCase))
            return null;

        using var file = TagLib.File.Create(filePath, ReadStyle.None);
        var pictures = file.Tag.Pictures.Where(p => p.MimeType != "-->" && p.Data.Count > 0);
        var cover = pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? pictures.FirstOrDefault();
        return cover?.Data.Data;
    }
}
