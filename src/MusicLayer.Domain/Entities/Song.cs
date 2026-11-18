namespace MusicLayer.Domain.Entities;

public sealed record Song
{
    public Song(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
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
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);
}
