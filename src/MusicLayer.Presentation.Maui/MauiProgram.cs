using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using MusicLayer.Shared.Lyrics;
using MusicLayer.Shared.Metadata;
using MusicLayer.Presentation.Maui.Platforms.Android;

namespace MusicLayer.Presentation.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<LibraryTrackList, LibraryTrackListHandler>());
        builder.Services.AddSingleton<TagLibTrackMetadataReader>();
        builder.Services.AddSingleton<AndroidPlayback>();
        builder.Services.AddSingleton<LocalLrcStore>();
        builder.Services.AddSingleton<ILocalLyricsReader>(services => services.GetRequiredService<LocalLrcStore>());
        builder.Services.AddSingleton(_ => CreateLyricsHttpClient());
        builder.Services.AddSingleton<LrclibClient>();
        builder.Services.AddSingleton<ILyricsCache>(_ => new JsonLyricsCache(Path.Combine(FileSystem.CacheDirectory, "lyrics")));
        builder.Services.AddSingleton<ILyricsProvider>(services => new CachedLyricsProvider(
            services.GetRequiredService<LrclibClient>(), services.GetRequiredService<ILyricsCache>()));
        builder.Services.AddSingleton<LoadTrackLyrics>();
        builder.Services.AddTransient<LyricsPage>();
        builder.Services.AddTransient<PlaybackPage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<AppShell>();
        return builder.Build();
    }

    private static HttpClient CreateLyricsHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://lrclib.net"),
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Tonic/{AppInfo.VersionString} (https://github.com/HenC1709/MusicLayerr)");
        return client;
    }
}

public sealed class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        this.services = services;
        Resources["TonicWordmark"] = new Style(typeof(Label))
        {
            Setters =
            {
                new Setter { Property = Label.FontSizeProperty, Value = 22d },
                new Setter { Property = Label.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#F2F8F7") },
                new Setter { Property = Label.VerticalOptionsProperty, Value = LayoutOptions.Center },
                new Setter { Property = Label.FontAutoScalingEnabledProperty, Value = true },
                new Setter { Property = Label.MaxLinesProperty, Value = 1 },
                new Setter { Property = Label.LineBreakModeProperty, Value = LineBreakMode.TailTruncation }
            }
        };
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(services.GetRequiredService<AppShell>());
}
