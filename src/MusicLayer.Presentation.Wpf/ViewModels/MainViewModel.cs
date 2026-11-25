using System.IO;
using System.Windows.Media.Imaging;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public sealed class MainViewModel(IAudioPlayer player, ScanMusicFolder scanner, PlaybackSession session,
    PausePlayback pause, ResumePlayback resume, StopPlayback stop, SetPlaybackVolume setVolume, ICoverArtReader coverReader,
    LoadTrackLyrics loadLyrics, SeekPlayback seek, IPlaybackPreferencesRepository preferences,
    IPlaylistRepository? playlistRepository = null)
    : MainViewModel<BitmapSource>(player, scanner, session, pause, resume, stop, setVolume, coverReader,
        loadLyrics, seek, preferences, DecodeBitmap, playlistRepository)
{
    private static BitmapSource DecodeBitmap(byte[] bytes, int width)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
