namespace MusicLayer.Domain.Entities;

public sealed record Song(string FilePath)
{
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);
}
