namespace MusicLayer.Domain.Entities;

public sealed record Playlist(Guid Id, string Name, IReadOnlyList<string> TrackPaths)
{
    public string? Genre { get; init; }
    public string? Description { get; init; }
    public byte[]? Cover { get; init; }
}
