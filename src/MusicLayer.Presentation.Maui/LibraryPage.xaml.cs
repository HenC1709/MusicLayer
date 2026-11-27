using System.Text.Json;
using MusicLayer.Domain.Entities;
using MusicLayer.Presentation.Maui.Platforms.Android;
using MusicLayer.Shared.Library;
using MusicLayer.Shared.Metadata;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Maui;

public sealed record LibraryTrack(SongRow Row, string? ArtworkPath, string? ContentUri = null)
{
    public string Title { get; } = Row.Song.DisplayName;
    public string Format { get; } = Row.FileFormat;
    public string Duration { get; } = Row.DurationText;
    public string Credits { get; } =
        $"{Row.Song.Metadata?.Artist ?? "Artista desconocido"} · {Row.Song.Metadata?.Album ?? "Álbum desconocido"}";
    public string AccessibilityLabel { get; } =
        $"{Row.Song.DisplayName}, {Row.Song.Metadata?.Artist ?? "Artista desconocido"} · {Row.Song.Metadata?.Album ?? "Álbum desconocido"}, {Row.FileFormat}";

    internal int CachedTextWidth { get; set; } = -1;
    internal string CachedTitle { get; set; } = "";
    internal string CachedCredits { get; set; } = "";
    internal float CachedDurationWidth { get; set; } = -1f;
}

public partial class LibraryPage : ContentPage
{
    private static readonly string[] SortValues = ["Título", "Artista", "Álbum", "Duración", "Orden de carpeta"];
    private readonly TagLibTrackMetadataReader metadata;
    private readonly AndroidPlayback playback;
    private readonly LibraryBrowser library = new();
    private readonly LibraryCatalogStore catalog = new(Path.Combine(FileSystem.AppDataDirectory, "library.json"));
    private IReadOnlyList<LibraryTrack> tracks = [];
    private Task? catalogLoad;
    private bool catalogAvailable;
    private bool updatingGroups;
    private int stateVersion;

    internal event EventHandler? PlaybackRequested;

    public LibraryPage(TagLibTrackMetadataReader metadata, AndroidPlayback playback)
    {
        InitializeComponent();
        this.metadata = metadata;
        this.playback = playback;
        MiniPlayer.Connect(playback);
        SortPicker.ItemsSource = new[] { "Título", "Artista", "Álbum", "Duración", "Importación" };
        SortPicker.SelectedIndex = 0;
        UpdateFilters();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        playback.Changed += UpdatePlayback;
        UpdatePlayback(this, EventArgs.Empty);
        _ = catalogLoad ??= RestoreCatalogAsync();
    }

    protected override void OnDisappearing()
    {
        playback.Changed -= UpdatePlayback;
        base.OnDisappearing();
    }

    private async Task RestoreCatalogAsync()
    {
        SetControlsEnabled(false);
        SetState("Recuperando biblioteca…", loading: true);
        try
        {
            var saved = await Task.Run(async () => CreateTracks(await catalog.LoadAsync()));
            ReplaceTracks(saved);
            catalogAvailable = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetState("No se pudo recuperar la biblioteca", "Podés volver a importar tus archivos.",
                error: true, importAction: "Importar canciones");
        }
        finally { SetControlsEnabled(true); }
    }

    private async void ChooseSongs(object? sender, EventArgs e) => await ImportSongsAsync();

    internal async Task ImportSongsAsync()
    {
        await (catalogLoad ??= RestoreCatalogAsync());
        if (!RetryImport.IsEnabled) return;
        SetControlsEnabled(false);
        bool selectionRead = false;
        try
        {
            var activity = Platform.CurrentActivity as MainActivity
                ?? throw new IOException("El selector necesita una ventana activa.");
            var files = await activity.PickAudioAsync();
            if (files is not { Length: > 0 }) return;

            string reading = files.Length == 1 ? "Leyendo 1 archivo." : $"Leyendo {files.Length} archivos.";
            SetState("Importando canciones…", reading, loading: true);
            selectionRead = true;
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
            catalogAvailable = true;
            int skipped = files.Length - tracks.Count;
            string summary = $"{library.ResultCount} en tu biblioteca.";
            if (skipped > 0)
                summary += skipped == 1 ? " No se pudo importar 1 archivo." : $" No se pudieron importar {skipped} archivos.";

            SetState("Importación completada", summary);
            int successState = stateVersion;
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(4), () =>
            {
                if (stateVersion == successState) SetState(null);
            });
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
        finally
        {
            SetControlsEnabled(true);
            if (catalogAvailable && selectionRead)
                AudioDocuments.KeepOnly(tracks.Select(track => track.ContentUri).Concat(playback.Queue.Select(track => track.ContentUri)));
        }
    }

    private async Task<List<LibraryCatalogTrack>> ReadTracksAsync(string[] files)
    {
        List<LibraryCatalogTrack> result = [];

        foreach (var file in files)
        {
            try
            {
                using var uri = global::Android.Net.Uri.Parse(file);
                if (uri is null) continue;
                string name = AudioDocuments.GetName(uri);
                if (Path.GetExtension(name).ToLowerInvariant() is not (".mp3" or ".wav" or ".flac")) continue;
                await using var source = AudioDocuments.Open(uri);
                var tags = await AudioDocuments.ReadSeekableAsync(source, stream => metadata.ReadSelection(name, stream));
                AudioDocuments.Keep(uri);
                result.Add(new LibraryCatalogTrack(name, tags.Metadata, CoverThumbnail.Save(tags.Cover)) { ContentUri = file });
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
            return new LibraryTrack(new SongRow(new Song(key, item.Metadata), index), CoverThumbnail.GetPath(item.CoverFileName), item.ContentUri);
        }).ToArray();
        foreach (var path in tracks.Select(t => t.ArtworkPath).OfType<string>().Distinct().Take(LibraryCoverCache.Capacity))
            LibraryCoverCache.Load(path);
        return tracks;
    }

    private void ReplaceTracks(IReadOnlyList<LibraryTrack> updated)
    {
        tracks = updated;
        library.ReplaceRows(tracks.Select(track => track.Row).ToArray());
        UpdateFilters();
        if (!string.IsNullOrEmpty(Search.Text)) Search.Text = "";
        else RefreshResults();
    }

    private void SetControlsEnabled(bool enabled)
    {
        Header.SetLibraryActionsEnabled(enabled);
        RetryImport.IsEnabled = Search.IsEnabled = ClearSearch.IsEnabled = enabled;
        LibraryTools.IsEnabled = enabled;
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

    private void SelectSection(object? sender, EventArgs e)
    {
        if (sender is not Button { Text: { } section } || section == library.Section) return;
        library.Section = section;
        UpdateFilters();
        RefreshResults();
    }

    private void UpdateFilters()
    {
        foreach (var button in new[] { SongsFilter, ArtistsFilter, AlbumsFilter, GenresFilter })
        {
            bool selected = button.Text == library.Section;
            button.BackgroundColor = Color.FromArgb(selected ? "#25E8A4" : "#0B1D22");
            button.TextColor = Color.FromArgb(selected ? "#041410" : "#9AB9B5");
            SemanticProperties.SetDescription(button, selected ? $"{button.Text}, seleccionado" : button.Text);
        }

        GroupBox.IsVisible = library.IsGrouped && library.Groups.Count > 0;
        updatingGroups = true;
        GroupPicker.ItemsSource = new[] { "Todos" }.Concat(library.Groups).ToArray();
        GroupPicker.SelectedIndex = 0;
        updatingGroups = false;
    }

    private void SelectGroup(object? sender, EventArgs e)
    {
        if (updatingGroups) return;
        int index = GroupPicker.SelectedIndex;
        library.Group = index > 0 && index <= library.Groups.Count ? library.Groups[index - 1] : null;
        RefreshResults();
    }

    private void SelectSort(object? sender, EventArgs e)
    {
        int index = SortPicker.SelectedIndex;
        if ((uint)index >= (uint)SortValues.Length || library.SortOrder == SortValues[index]) return;
        library.SortOrder = SortValues[index];
        RefreshResults();
    }

    private void PlayAll(object? sender, EventArgs e)
    {
        var available = TrackList.Items.Where(track => track.ContentUri is not null).ToArray();
        if (available.Length == 0) return;
        int index = playback.Shuffle ? Random.Shared.Next(available.Length) : 0;
        PlayTrack(this, available[index]);
    }

    private void ToggleShuffle(object? sender, EventArgs e) => playback.ToggleShuffle();

    private void UpdatePlayback(object? sender, EventArgs e)
    {
        TrackList.ActiveTrack = playback.Current;
        ShuffleButton.BackgroundColor = Color.FromArgb(playback.Shuffle ? "#14564A" : "#0B1D22");
        SemanticProperties.SetDescription(ShuffleButton, playback.Shuffle ? "Modo aleatorio activado" : "Modo aleatorio desactivado");
    }

    private void PlayTrack(object? sender, LibraryTrack track)
    {
        playback.Play(TrackList.Items, track);
        PlaybackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenPlayback(object? sender, EventArgs e) => PlaybackRequested?.Invoke(this, e);

    private void SearchChanged(object? sender, TextChangedEventArgs e)
    {
        library.SearchText = e.NewTextValue ?? "";
        RefreshResults();
    }

    private void RefreshResults()
    {
        LibrarySummary.Text = library.Summary;
        TrackList.Items = library.Rows.Select(row => tracks[row.Index]).ToArray();
        LibraryTools.IsVisible = tracks.Count > 0;
        PlayAllButton.IsEnabled = TrackList.Items.Any(track => track.ContentUri is not null);

        bool filtered = library.Group is not null || !string.IsNullOrWhiteSpace(library.SearchText);
        ResultCount.IsVisible = filtered && library.HasResults;
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
        stateVersion++;
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
