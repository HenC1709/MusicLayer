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
        builder.Services.AddTransient<PlaybackPage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<AppShell>();
        return builder.Build();
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
