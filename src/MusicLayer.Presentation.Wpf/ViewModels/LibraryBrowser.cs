using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Presentation.Wpf.ViewModels;

public sealed class SongRow(Song song, int index) : INotifyPropertyChanged
{
    public Song Song { get; } = song;
    public int Index { get; } = index;
    public int Number => Index + 1;
    public string FileFormat => Song.FileFormat;
    public string DurationText => Song.Metadata?.Duration is { } duration ? MainViewModel.FormatPlaybackTime(duration) : "—";
    public bool IsFavorite
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(IsFavorite)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LibraryBrowser : INotifyPropertyChanged
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;
    private readonly HashSet<string> favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private long playOrder;
    private IReadOnlyList<SongRow> source = [];
    internal event Action<string, bool>? FavoriteChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SongRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Groups { get; private set; } = [];
    public bool IsGrouped => Section is "Artistas" or "Álbumes" or "Géneros";
    public bool HasResults => Rows.Count > 0;
    public string Summary { get; private set; } = "0 canciones";
    public string ResultCount => CountLabel(Rows.Count, "canción", "canciones");
    public string SearchText
    {
        get;
        set
        {
            if (field == value) return;
            field = value ?? "";
            Notify();
            RefreshRows();
        }
    } = "";

    public string Section
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Notify();
            Notify(nameof(IsGrouped));
            RefreshGroups();
            RefreshRows();
        }
    } = "Canciones";

    public string SortOrder
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Notify();
            RefreshRows();
        }
    } = "Título";

    private string? group;
    public string? Group
    {
        get => group;
        set
        {
            if (group == value) return;
            group = value;
            Notify();
            RefreshRows();
        }
    }

    internal void RestoreFavorites(IEnumerable<string> paths) => favorites.UnionWith(paths);

    internal void ReplaceRows(IReadOnlyList<SongRow> rows)
    {
        foreach (var row in source) row.PropertyChanged -= OnFavoriteChanged;
        source = rows;
        foreach (var row in rows)
        {
            row.IsFavorite = favorites.Contains(row.Song.FilePath);
            row.PropertyChanged += OnFavoriteChanged;
        }
        int artists = DistinctNames(rows.Select(r => r.Song.Metadata?.Artist)).Count();
        int albums = DistinctNames(rows.Select(r => r.Song.Metadata?.Album)).Count();
        Summary = string.Join("  ·  ", CountLabel(rows.Count, "canción", "canciones"),
            CountLabel(albums, "álbum", "álbumes"), CountLabel(artists, "artista", "artistas"));
        Notify(nameof(Summary));
        RefreshGroups();
        RefreshRows();
    }

    internal void RecordPlay(Song song)
    {
        lastPlayed[song.FilePath] = ++playOrder;
        if (Section == "Recientes") RefreshRows();
    }

    private void OnFavoriteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SongRow row || e.PropertyName != nameof(SongRow.IsFavorite)) return;
        var path = row.Song.FilePath;
        bool changed = row.IsFavorite ? favorites.Add(path) : favorites.Remove(path);
        if (!changed) return;
        foreach (var other in source)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(other.Song.FilePath, path))
                other.IsFavorite = row.IsFavorite;
        }
        if (Section == "Favoritas") RefreshRows();
        FavoriteChanged?.Invoke(path, row.IsFavorite);
    }

    private void RefreshGroups()
    {
        group = null;
        Groups = IsGrouped ? DistinctNames(source.Select(GroupName)).Order(Names).ToArray() : [];
        Notify(nameof(Groups));
        Notify(nameof(Group));
    }

    private string? GroupName(SongRow row) => Section switch
    {
        "Artistas" => row.Song.Metadata?.Artist,
        "Álbumes" => row.Song.Metadata?.Album,
        "Géneros" => row.Song.Metadata?.Genre,
        _ => null
    };

    private void RefreshRows()
    {
        IEnumerable<SongRow> rows = source;
        if (Section == "Favoritas") rows = rows.Where(r => r.IsFavorite);
        if (Section == "Recientes") rows = rows.Where(r => lastPlayed.ContainsKey(r.Song.FilePath));
        if (IsGrouped && Group is { } selected) rows = rows.Where(r => Names.Equals(GroupName(r), selected));
        string query = SearchText.Trim();
        if (query.Length > 0) rows = rows.Where(r =>
            Matches(r.Song.DisplayName, query) || Matches(r.Song.Metadata?.Artist, query) ||
            Matches(r.Song.Metadata?.Album, query) || Matches(r.Song.Metadata?.Genre, query));

        IOrderedEnumerable<SongRow> sorted = SortOrder switch
        {
            "Artista" => rows.OrderBy(r => r.Song.Metadata?.Artist, Names),
            "Álbum" => rows.OrderBy(r => r.Song.Metadata?.Album, Names),
            "Duración" => rows.OrderBy(r => r.Song.Metadata?.Duration ?? TimeSpan.MaxValue),
            "Orden de carpeta" => rows.OrderBy(r => r.Index),
            _ => rows.OrderBy(r => r.Song.DisplayName, Names)
        };
        if (Section == "Recientes")
            sorted = sorted.OrderByDescending(r => lastPlayed[r.Song.FilePath]);
        Rows = sorted.ThenBy(r => r.Song.DisplayName, Names).ThenBy(r => r.Index).ToArray();
        Notify(nameof(Rows));
        Notify(nameof(ResultCount));
        Notify(nameof(HasResults));
    }

    private static bool Matches(string? text, string query)
        => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static IEnumerable<string> DistinctNames(IEnumerable<string?> names)
        => names.OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(Names);

    private static string CountLabel(int count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new(name));
}
