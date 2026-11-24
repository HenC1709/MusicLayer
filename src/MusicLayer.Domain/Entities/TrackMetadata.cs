namespace MusicLayer.Domain.Entities;

public sealed record TrackMetadata(string? Title, string? Artist, string? Album)
{
    public string? Genre { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? AudioBitrate { get; init; }
    public int? AudioSampleRate { get; init; }
}
