using MusicLayer.Application.Abstractions;
using MusicLayer.Shared.Metadata;

namespace MusicLayer.Presentation.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<ITrackMetadataReader, TagLibTrackMetadataReader>();
        builder.Services.AddSingleton<ICoverArtReader, TagLibCoverArtReader>();
        builder.Services.AddTransient<LibraryPage>();
        return builder.Build();
    }
}

public sealed class App(IServiceProvider services) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(services.GetRequiredService<LibraryPage>());
}
