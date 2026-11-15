using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using MusicLayer.Application.UseCases.Library;
using MusicLayer.Application.UseCases.Playback;
using MusicLayer.Domain.Entities;
using MusicLayer.Infrastructure.Audio;
using MusicLayer.Infrastructure.FileSystem;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly NAudioPlayer player = new();
    private readonly ScanMusicFolder scanFolder = new(new MusicFileScanner());
    private readonly PlaySong playSong;
    private readonly CancellationTokenSource lifetime = new();
    private bool closing;
    private bool canClose;

    public MainWindow()
    {
        InitializeComponent();
        playSong = new PlaySong(player);
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegir carpeta de música" };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var songs = await scanFolder.ExecuteAsync(dialog.FolderName, lifetime.Token);
            SongsList.ItemsSource = songs;
            StatusText.Text = songs.Count == 0 ? "La carpeta no contiene archivos MP3, WAV o FLAC." : $"{songs.Count} canciones encontradas.";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { StatusText.Text = $"No se pudo leer la carpeta: {error.Message}"; }
        finally { if (!closing) SetBusy(false); }
    }

    private async void PlaySelected(object sender, RoutedEventArgs e)
    {
        if (SongsList.SelectedItem is not Song song)
        {
            StatusText.Text = "Selecciona una canción.";
            return;
        }
        SetBusy(true);
        try
        {
            await playSong.ExecuteAsync(song, lifetime.Token);
            StatusText.Text = $"Reproduciendo: {song.DisplayName}";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error) { StatusText.Text = $"No se pudo reproducir el archivo: {error.Message}"; }
        finally { if (!closing) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        FolderButton.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!canClose)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (closing) return;
            closing = true;
            SetBusy(true);
            lifetime.Cancel();
            try { await player.DisposeAsync(); }
            finally
            {
                canClose = true;
                Close();
            }
            return;
        }
        base.OnClosing(e);
    }
}
