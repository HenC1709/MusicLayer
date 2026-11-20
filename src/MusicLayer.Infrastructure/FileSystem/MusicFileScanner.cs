using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Infrastructure.FileSystem;

public sealed class MusicFileScanner(ITrackMetadataReader metadataReader) : IMusicFileScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac"
    };

    public Task<IReadOnlyList<Song>> ScanAsync(string folder, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<Song>>(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);
            var songs = new List<Song>();
            var rootOptions = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var subtreeOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", rootOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is DirectoryInfo directory)
                {
                    try
                    {
                        foreach (var path in Directory.EnumerateFiles(directory.FullName, "*", subtreeOptions))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            AddIfSupported(path);
                        }
                    }
                    catch (DirectoryNotFoundException) { /* A subfolder disappeared during the scan. */ }
                }
                else
                    AddIfSupported(entry.FullName);
            }
            return songs.OrderBy(song => Path.GetFileNameWithoutExtension(song.FilePath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(song => song.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            void AddIfSupported(string path)
            {
                if (Extensions.Contains(Path.GetExtension(path)))
                    songs.Add(new Song(path, metadataReader.Read(path)));
            }
        }, cancellationToken);
}
