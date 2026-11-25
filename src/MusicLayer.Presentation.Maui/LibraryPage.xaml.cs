using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Maui;

public sealed record LibraryTrack(SongRow Row, ImageSource? Artwork)
{
    public string Artist => Row.Song.Metadata?.Artist ?? "Artista desconocido";
}

public partial class LibraryPage : ContentPage
{
    private readonly ITrackMetadataReader metadata;
    private readonly ICoverArtReader covers;
    private readonly LibraryBrowser library = new();
    private IReadOnlyList<LibraryTrack> tracks = [];

    public LibraryPage(ITrackMetadataReader metadata, ICoverArtReader covers)
    {
        InitializeComponent();
        this.metadata = metadata;
        this.covers = covers;
    }

    private async void ChooseSongs(object? sender, EventArgs e)
    {
        if (!ImportButton.IsEnabled) return;
        ImportButton.IsEnabled = false;
        string previousStatus = Status.Text;
        try
        {
            var files = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Elegir canciones",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = ["audio/*", "application/flac", "application/x-flac"]
                })
            });
            var paths = files?.OfType<FileResult>().Select(file => file.FullPath).Distinct(StringComparer.Ordinal).ToArray();
            if (paths is not { Length: > 0 }) return;
            Status.Text = "Leyendo canciones…";
            var imported = await Task.Run(() => ReadTracks(paths));
            if (imported.Count == 0)
            {
                Status.Text = "No se encontraron pistas legibles en MP3, WAV o FLAC.";
                return;
            }
            tracks = imported.Select((track, index) => new LibraryTrack(new SongRow(track.Song, index),
                track.Cover is { } cover ? ImageSource.FromStream(() => new MemoryStream(cover, false)) : null)).ToArray();
            library.ReplaceRows(tracks.Select(track => track.Row).ToArray());
            Search.Text = "";
            Search.IsVisible = true;
            RefreshResults();
            int skipped = paths.Length - tracks.Count;
            if (skipped > 0) Status.Text += $" · Archivos omitidos: {skipped}";
        }
        catch (OperationCanceledException) { Status.Text = previousStatus; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PermissionException
                                      or Java.Lang.SecurityException or FeatureNotSupportedException)
        {
            Status.Text = "No se pudo acceder a la selección. Elegí los archivos otra vez.";
        }
        finally { ImportButton.IsEnabled = true; }
    }

    private List<(Song Song, byte[]? Cover)> ReadTracks(string[] paths)
    {
        List<(Song, byte[]?)> result = [];
        foreach (var path in paths)
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".mp3" or ".wav" or ".flac")) continue;
            if (!File.Exists(path)) continue;
            var song = new Song(path, metadata.Read(path));
            byte[]? thumbnail = null;
            try { thumbnail = CoverThumbnail.Create(covers.ReadEmbeddedCover(path)); }
            catch (Exception error) when (error is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                          or IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Keep the track when its embedded image cannot be read.
            }
            result.Add((song, thumbnail));
        }
        return result;
    }

    private void SearchChanged(object? sender, TextChangedEventArgs e)
    {
        library.SearchText = e.NewTextValue ?? "";
        RefreshResults();
    }

    private void RefreshResults()
    {
        TrackList.ItemsSource = library.Rows.Select(row => tracks[row.Index]).ToArray();
        Status.Text = library.HasResults ? library.ResultCount : "Ninguna canción coincide con la búsqueda.";
    }
}
