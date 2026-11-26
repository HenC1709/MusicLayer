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
        builder.Services.AddTransient<LibraryPage>();
        return builder.Build();
    }
}

public sealed class App(IServiceProvider services) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(services.GetRequiredService<LibraryPage>());
}
