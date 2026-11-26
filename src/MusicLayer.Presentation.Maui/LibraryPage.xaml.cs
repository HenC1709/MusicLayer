using System.Text.Json;
using MusicLayer.Domain.Entities;
using MusicLayer.Presentation.Maui.Platforms.Android;
using MusicLayer.Shared.Library;
using MusicLayer.Shared.Metadata;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Maui;

public sealed record LibraryTrack(SongRow Row, string? ArtworkPath)
{
    public string Title { get; } = Row.Song.DisplayName;
    public string Format { get; } = Row.FileFormat;
    public string Credits { get; } =
        $"{Row.Song.Metadata?.Artist ?? "Artista desconocido"} · {Row.Song.Metadata?.Album ?? "Álbum desconocido"}";
    public string AccessibilityLabel { get; } =
        $"{Row.Song.DisplayName}, {Row.Song.Metadata?.Artist ?? "Artista desconocido"} · {Row.Song.Metadata?.Album ?? "Álbum desconocido"}, {Row.FileFormat}";

    internal int CachedTextWidth { get; set; } = -1;
    internal string CachedTitle { get; set; } = "";
    internal string CachedCredits { get; set; } = "";
    internal float CachedFormatWidth { get; set; } = -1f;
}

public partial class LibraryPage : ContentPage
{
    private readonly TagLibTrackMetadataReader metadata;
    private readonly LibraryBrowser library = new();
    private readonly LibraryCatalogStore catalog = new(Path.Combine(FileSystem.AppDataDirectory, "library.json"));
    private IReadOnlyList<LibraryTrack> tracks = [];
    private bool catalogLoaded;

    public LibraryPage(TagLibTrackMetadataReader metadata)
    {
        InitializeComponent();
        this.metadata = metadata;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (catalogLoaded) return;

        catalogLoaded = true;
        SetControlsEnabled(false);
        SetState("Recuperando biblioteca…", loading: true);
        try
        {
            var saved = await Task.Run(async () => CreateTracks(await catalog.LoadAsync()));
            ReplaceTracks(saved);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetState("No se pudo recuperar la biblioteca", "Podés volver a importar tus archivos.",
                error: true, importAction: "Importar canciones");
        }
        finally { SetControlsEnabled(true); }
    }

    private async void ChooseSongs(object? sender, EventArgs e)
    {
        if (!ImportButton.IsEnabled) return;
        SetControlsEnabled(false);
        try
        {
            var selection = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Elegir canciones",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = ["audio/*", "application/flac", "application/x-flac"]
                })
            });
            var files = selection?.OfType<FileResult>().ToArray();
            if (files is not { Length: > 0 }) return;

            string reading = files.Length == 1 ? "Leyendo 1 archivo." : $"Leyendo {files.Length} archivos.";
            SetState("Importando canciones…", reading, loading: true);
            var imported = await Task.Run(() => ReadTracksAsync(files));
            if (imported.Count == 0)
            {
                SetState("No se pudo importar música",
                    "Elegí archivos MP3, WAV o FLAC que se puedan leer.", error: true, importAction: "Reintentar");
                return;
            }

            var prepared = await Task.Run(() => CreateTracks(imported));
            try { await catalog.SaveAsync(imported); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SetState("No se pudo guardar la biblioteca", "Se conservó la biblioteca anterior.",
                    error: true, importAction: "Reintentar");
                return;
            }

            ReplaceTracks(prepared);
            int skipped = files.Length - tracks.Count;
            string summary = $"{library.ResultCount} en tu biblioteca.";
            if (skipped > 0)
                summary += skipped == 1 ? " No se pudo importar 1 archivo." : $" No se pudieron importar {skipped} archivos.";

            SetState("Importación completada", summary);
        }
        catch (OperationCanceledException)
        {
            if (ImportProgress.IsRunning) RefreshResults();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PermissionException
                                      or Java.Lang.SecurityException or FeatureNotSupportedException)
        {
            SetState("No se pudo abrir la selección", "Volvé a elegir los archivos.",
                error: true, importAction: "Reintentar");
        }
        finally { SetControlsEnabled(true); }
    }

    private async Task<List<LibraryCatalogTrack>> ReadTracksAsync(FileResult[] files)
    {
        List<LibraryCatalogTrack> result = [];

        foreach (var file in files)
        {
            string name = Path.GetFileName(file.FileName);
            if (Path.GetExtension(name).ToLowerInvariant() is not (".mp3" or ".wav" or ".flac")) continue;

            try
            {
                await using var source = await file.OpenReadAsync();
                var tags = await ReadTagsAsync(name, source);
                result.Add(new LibraryCatalogTrack(name, tags.Metadata, CoverThumbnail.Save(tags.Cover)));
            }
            catch (Exception error) when (error is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                          or IOException or UnauthorizedAccessException or PermissionException
                                          or Java.IO.IOException or Java.Lang.SecurityException)
            {
                // Continue importing the remaining files when one selection cannot be read.
            }
        }

        return result;
    }

    private static LibraryTrack[] CreateTracks(IReadOnlyList<LibraryCatalogTrack> source)
    {
        var tracks = source.Select((item, index) =>
        {
            // Song derives its display name and format from a path. This catalog key does not point to audio.
            string key = Path.Combine(FileSystem.AppDataDirectory, "catalog", index.ToString(), Path.GetFileName(item.FileName));
            return new LibraryTrack(new SongRow(new Song(key, item.Metadata), index), CoverThumbnail.GetPath(item.CoverFileName));
        }).ToArray();
        foreach (var path in tracks.Select(t => t.ArtworkPath).OfType<string>().Distinct().Take(LibraryCoverCache.Capacity))
            LibraryCoverCache.Load(path);
        return tracks;
    }

    private void ReplaceTracks(IReadOnlyList<LibraryTrack> updated)
    {
        tracks = updated;
        library.ReplaceRows(tracks.Select(track => track.Row).ToArray());
        if (!string.IsNullOrEmpty(Search.Text)) Search.Text = "";
        else RefreshResults();
    }

    private void SetControlsEnabled(bool enabled)
    {
        ImportButton.IsEnabled = RetryImport.IsEnabled = enabled;
        SearchToggle.IsEnabled = Search.IsEnabled = ClearSearch.IsEnabled = enabled;
    }

    private async Task<(TrackMetadata? Metadata, byte[]? Cover)> ReadTagsAsync(string name, Stream source)
    {
        if (SupportsRandomAccess(source)) return metadata.ReadSelection(name, source);

        // Some document providers return pipes. TagLib needs a seekable stream.
        string temporaryPath = Path.Combine(FileSystem.CacheDirectory, $"audio-{Guid.NewGuid():N}.tmp");
        await using var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await source.CopyToAsync(temporary);
        return metadata.ReadSelection(name, temporary);
    }

    private static bool SupportsRandomAccess(Stream stream)
    {
        if (!stream.CanSeek) return false;

        try
        {
            _ = stream.Length;
            return true;
        }
        catch (NotSupportedException) { return false; }
    }

    private void ToggleSearch(object? sender, EventArgs e)
    {
        SearchPanel.IsVisible = !SearchPanel.IsVisible;
        if (SearchPanel.IsVisible)
            Search.Focus();
        else
        {
            Search.Unfocus();
            Search.Text = "";
        }
    }

    private void ClearQuery(object? sender, EventArgs e) => Search.Text = "";

    private void SearchChanged(object? sender, TextChangedEventArgs e)
    {
        library.SearchText = e.NewTextValue ?? "";
        RefreshResults();
    }

    private void RefreshResults()
    {
        LibrarySummary.Text = library.Summary;
        TrackList.Items = library.Rows.Select(row => tracks[row.Index]).ToArray();

        bool searching = !string.IsNullOrWhiteSpace(library.SearchText);
        ResultCount.IsVisible = searching && library.HasResults;
        ResultCount.Text = library.ResultCount;
        if (tracks.Count == 0)
            SetState("Tu biblioteca está vacía", "Importá canciones en MP3, WAV o FLAC para empezar.",
                importAction: "Importar canciones");
        else if (!library.HasResults)
            SetState("Sin resultados", "Probá con otro título, artista o álbum.", canClearSearch: true);
        else
            SetState(null);
    }

    private void SetState(string? title, string? message = null, bool loading = false, bool error = false,
        string? importAction = null, bool canClearSearch = false)
    {
        StateCard.IsVisible = title is not null;
        StateTitle.Text = title;
        StateMessage.Text = message;
        StateMessage.IsVisible = message is not null;
        StateCard.Stroke = Color.FromArgb(error ? "#8F4E47" : "#20483F");
        StateTitle.TextColor = Color.FromArgb(error ? "#FF9D91" : "#F2F8F7");
        StateIcon.IsVisible = !loading;
        StateIcon.Source = canClearSearch ? "search.png" : "folder_open.png";
        ImportProgress.IsRunning = ImportProgress.IsVisible = loading;
        RetryImport.Text = importAction;
        RetryImport.IsVisible = importAction is not null;
        ClearSearch.IsVisible = canClearSearch;
    }
}
