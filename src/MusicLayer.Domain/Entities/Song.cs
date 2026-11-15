namespace MusicLayer.Domain.Entities;

public sealed record Song
{
    public Song(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);
}
