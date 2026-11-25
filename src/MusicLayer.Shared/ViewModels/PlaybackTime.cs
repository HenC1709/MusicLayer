namespace MusicLayer.Shared.ViewModels;

internal static class PlaybackTime
{
    public static string Format(TimeSpan time) => time.TotalHours < 1
        ? time.ToString(@"mm\:ss")
        : FormattableString.Invariant($@"{(long)time.TotalHours}:{time:mm\:ss}");
}
