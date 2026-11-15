using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.Abstractions;

public interface IMusicFileScanner
{
    Task<IReadOnlyList<Song>> ScanAsync(string folder, CancellationToken cancellationToken = default);
}
