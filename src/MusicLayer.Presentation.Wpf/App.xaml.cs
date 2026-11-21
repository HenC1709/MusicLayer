using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Infrastructure.Audio;
using MusicLayer.Infrastructure.FileSystem;
using MusicLayer.Infrastructure.Lyrics;
using MusicLayer.Infrastructure.Metadata;
using MusicLayer.Presentation.Wpf.ViewModels;
using MusicLayer.Presentation.Wpf.Views;

namespace MusicLayer.Presentation.Wpf;

public partial class App : System.Windows.Application
{
    private ServiceProvider? services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        services = new ServiceCollection()
            .AddSingleton<IAudioPlayer, NAudioPlayer>()
            .AddSingleton<IMusicFileScanner, MusicFileScanner>()
            .AddSingleton<ITrackMetadataReader, TagLibTrackMetadataReader>()
            .AddSingleton<ICoverArtReader, TagLibCoverArtReader>()
            .AddSingleton(_ => CreateLyricsHttpClient())
            .AddSingleton<LrclibClient>()
            .AddSingleton<ILyricsCache>(_ => new JsonLyricsCache(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicLayer", "LyricsCache")))
            .AddSingleton<ILyricsProvider>(provider => new CachedLyricsProvider(
                provider.GetRequiredService<LrclibClient>(), provider.GetRequiredService<ILyricsCache>()))
            .AddSingleton<ScanMusicFolder>()
            .AddSingleton<PlaySong>()
            .AddSingleton<PausePlayback>()
            .AddSingleton<ResumePlayback>()
            .AddSingleton<StopPlayback>()
            .AddSingleton<SetPlaybackVolume>()
            .AddSingleton<SeekPlayback>()
            .AddSingleton<PlaybackSession>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();
        MainWindow = services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    internal async Task ReleaseServicesAsync()
    {
        if (services is not null) await services.DisposeAsync();
    }

    private static HttpClient CreateLyricsHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            BaseAddress = new Uri("https://lrclib.net"),
            Timeout = TimeSpan.FromSeconds(8)
        };
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicLayer/{version} (https://github.com/HenC1709/MusicLayerr)");
        return client;
    }
}
