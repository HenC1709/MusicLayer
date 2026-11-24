namespace MusicLayer.Application.Abstractions;

public sealed record PlaybackPreferences(string? LibraryFolder, int VolumePercent)
{
    public PlayerOptions Options { get; init; } = new();
    public PlaybackBookmark? Bookmark { get; init; }
}

public sealed record PlayerOptions(bool RememberPosition = false, bool AutoAdvance = true,
    bool FollowLyrics = true, bool DimInactiveLyrics = true);

public sealed record PlaybackBookmark(string FilePath, TimeSpan Position);
