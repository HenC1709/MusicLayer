using System.Windows.Controls;
using System.Windows.Media;
using MusicLayer.Presentation.Wpf.ViewModels;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

internal sealed class SongThumbnailLoader
{
    private readonly SemaphoreSlim slots = new(3);

    public static void Reset(Image image)
    {
        (image.Tag as CancellationTokenSource)?.Cancel();
        image.Tag = null;
        image.Source = null;
    }

    public async void Refresh(Image image, MainViewModel? model, ImageSource fallback)
    {
        Reset(image);
        if (image.DataContext is not SongRow row || model is null) return;
        image.Source = fallback;
        using var request = new CancellationTokenSource();
        image.Tag = request;
        try
        {
            await slots.WaitAsync(request.Token);
            try
            {
                var art = await model.ReadThumbnailAsync(row.Song, request.Token);
                if (!request.IsCancellationRequested && art is not null) image.Source = art;
            }
            finally { slots.Release(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { /* An unreadable embedded picture keeps the fallback artwork. */ }
        finally { if (image.Tag == request) image.Tag = null; }
    }
}
