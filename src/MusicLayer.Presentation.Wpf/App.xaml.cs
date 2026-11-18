using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Infrastructure.Audio;
using MusicLayer.Infrastructure.FileSystem;
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
            .AddSingleton<ScanMusicFolder>()
            .AddSingleton<PlaySong>()
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
}
