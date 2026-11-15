using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.UseCases.Library;

public sealed class ScanMusicFolder(IMusicFileScanner scanner)
{
    public Task<IReadOnlyList<Song>> ExecuteAsync(string folder, CancellationToken cancellationToken = default)
        => scanner.ScanAsync(folder, cancellationToken);
}
