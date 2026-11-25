using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class PlaylistEditor : Window
{
    private readonly PlaylistsViewModel<BitmapSource> model;
    private readonly Guid? playlistId;
    private byte[]? cover;
    private bool saving;

    public PlaylistEditor(Window owner, PlaylistsViewModel<BitmapSource> model, PlaylistCard<BitmapSource>? playlist)
    {
        InitializeComponent();
        Owner = owner;
        Fields.MaxHeight = Math.Clamp(owner.ActualHeight - 150, 160, 340);
        this.model = model;
        playlistId = playlist?.Id;
        Title = Heading.Text = playlist is null ? "Crear playlist" : "Editar playlist";
        SaveButton.Content = playlist is null ? "Crear" : "Guardar";
        NameBox.Text = playlist?.Name ?? "";
        GenreBox.Text = playlist?.Genre ?? "";
        DescriptionBox.Text = playlist?.Description ?? "";
        SetCover(playlist?.Data.Cover, playlist?.CustomCover);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (!model.CanEdit || saving) return;
        ShowError(null);
        saving = true;
        Form.IsEnabled = false;
        try
        {
            await model.SaveDetailsAsync(playlistId, NameBox.Text, GenreBox.Text, DescriptionBox.Text, cover);
        }
        finally
        {
            saving = false;
            Form.IsEnabled = true;
        }
        if (model.Error is { } error)
        {
            ShowError(error);
            NameBox.Focus();
        }
        else if (model.CanEdit) DialogResult = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (saving) e.Cancel = true;
    }

    private void ChooseCover(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir foto de portada",
            Filter = "Imágenes JPEG y PNG|*.jpg;*.jpeg;*.png"
        };
        if (dialog.ShowDialog(this) == true) LoadCover(dialog.FileName);
    }

    private void LoadCover(string fileName)
    {
        try
        {
            using var stream = File.OpenRead(fileName);
            if (stream.Length > 20 * 1024 * 1024)
            {
                ShowError("Elige una foto de hasta 20 MB.");
                return;
            }
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var landscape = frame.PixelWidth >= frame.PixelHeight;
            stream.Position = 0;
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.StreamSource = stream;
            preview.CacheOption = BitmapCacheOption.OnLoad;
            if (landscape) preview.DecodePixelWidth = Math.Min(512, frame.PixelWidth);
            else preview.DecodePixelHeight = Math.Min(512, frame.PixelHeight);
            preview.EndInit();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            using var output = new MemoryStream();
            encoder.Save(output);
            SetCover(output.ToArray(), preview);
            ShowError(null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            ShowError("No se pudo abrir la foto. Elige una imagen JPEG o PNG válida.");
        }
    }

    private void RemoveCover(object sender, RoutedEventArgs e)
    {
        SetCover(null, null);
        ShowError(null);
    }

    private void SetCover(byte[]? bytes, BitmapSource? preview)
    {
        cover = bytes;
        CoverPreview.Source = preview;
        RemoveCoverButton.Visibility = bytes is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowError(string? message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        if (message is not null)
            UIElementAutomationPeer.CreatePeerForElement(ErrorMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
