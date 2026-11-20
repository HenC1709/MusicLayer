namespace MusicLayer.Application.Abstractions;

public interface ICoverArtReader
{
    byte[]? ReadEmbeddedCover(string filePath);
}
