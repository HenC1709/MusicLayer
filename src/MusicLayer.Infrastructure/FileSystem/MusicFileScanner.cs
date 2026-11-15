using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Infrastructure.FileSystem;

public sealed class MusicFileScanner : IMusicFileScanner
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
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Extensions.Contains(Path.GetExtension(file)))
                    songs.Add(new Song(Path.GetFullPath(file)));
            }
            return songs.OrderBy(song => song.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }, cancellationToken);
}
