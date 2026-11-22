namespace MusicLayer.Domain.Entities;

public sealed record Song
{
    public Song(string filePath, TrackMetadata? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Metadata = metadata;
    }

    public string FolderName
    {
        get
        {
            var directory = Path.GetDirectoryName(FilePath) ?? string.Empty;
            var name = Path.GetFileName(directory);
            return name.Length > 0 ? name : directory;  // si está en la raíz del disco (C:\), usa la ruta
        }
    }

    public string FilePath { get; }
    public string FileFormat => Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();
    public TrackMetadata? Metadata { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Metadata?.Title)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Metadata.Title;
}
