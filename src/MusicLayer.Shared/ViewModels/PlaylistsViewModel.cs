using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Shared.ViewModels;

public sealed record PlaylistTrack(string FilePath, SongRow? Row)
{
    public string Title => Row?.Song.DisplayName ?? Path.GetFileNameWithoutExtension(FilePath);
    public string? Artist => Row?.Song.Metadata?.Artist;
    public string Duration => Row?.DurationText ?? "—";
    public bool IsAvailable => Row is not null;
}

public sealed record PlaylistCard<TCover>(Playlist Data, IReadOnlyList<PlaylistTrack> Tracks, TCover? CustomCover)
    where TCover : class
{
    public Guid Id => Data.Id;
    public string Name => Data.Name;
    public string Summary => Tracks.Count == 1 ? "1 canción" : $"{Tracks.Count} canciones";
    public string? Genre => Data.Genre;
    public string? Description => Data.Description;
    public string CardSummary => Genre is null ? Summary : $"{Genre} · {Summary}";
    public SongRow? CoverRow => CustomCover is null ? Tracks.FirstOrDefault(track => track.IsAvailable)?.Row : null;
    public bool CanPlay => Tracks.Any(track => track.IsAvailable);
}

public sealed class PlaylistsViewModel<TCover>(IPlaylistRepository repository, Func<byte[]?, int, TCover?> decodeCover,
    CancellationToken cancellationToken) : INotifyPropertyChanged where TCover : class
{
    private readonly List<Playlist> saved = [];
    private Dictionary<string, SongRow> byPath = new(StringComparer.OrdinalIgnoreCase);
    private bool loaded;
    private bool busy;

    public event PropertyChangedEventHandler? PropertyChanged;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    public bool CanEdit => loaded && !busy && !cancellationToken.IsCancellationRequested;
    public string? Error { get; private set; }
    public IReadOnlyList<SongRow> LibraryRows { get; private set; } = [];
    public IReadOnlyList<PlaylistCard<TCover>> Cards { get; private set; } = [];
    public PlaylistCard<TCover>? Selected { get; private set; }

    internal Task InitializeAsync() => RunAsync(async () =>
    {
        var playlists = await repository.ReadAsync(cancellationToken);
        saved.Clear();
        saved.AddRange(playlists);
        loaded = true;
        RefreshCards(Selected?.Id);
    });

    internal void SetLibrary(IReadOnlyList<SongRow> rows)
    {
        LibraryRows = rows;
        byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) byPath.TryAdd(row.Song.FilePath, row);
        Notify(nameof(LibraryRows));
        RefreshCards(Selected?.Id);
    }

    public void Open(Guid? id)
    {
        Selected = Cards.FirstOrDefault(card => card.Id == id);
        Notify(nameof(Selected));
    }

    public Task SaveDetailsAsync(Guid? id, string name, string genre, string description, byte[]? cover)
    {
        if (!CanEdit) return Task.CompletedTask;
        var data = id is Guid existing ? saved.Find(playlist => playlist.Id == existing)
            : new Playlist(Guid.NewGuid(), "", []);
        if (data is null) return Task.CompletedTask;
        name = name.Trim();
        genre = genre.Trim();
        description = description.Trim();
        if (name.Length is 0 or > 80)
        {
            SetError("Escribe un nombre de 1 a 80 caracteres.");
            return Task.CompletedTask;
        }
        if (saved.Any(playlist => playlist.Id != data.Id && StringComparer.CurrentCultureIgnoreCase.Equals(playlist.Name, name)))
        {
            SetError("Ya existe una playlist con ese nombre.");
            return Task.CompletedTask;
        }
        return SaveAsync(data with
        {
            Name = name,
            Genre = genre.Length == 0 ? null : genre,
            Description = description.Length == 0 ? null : description,
            Cover = cover
        });
    }

    public Task AddAsync(SongRow row)
    {
        if (!CanEdit || Selected is not { } card || !byPath.ContainsKey(row.Song.FilePath)) return Task.CompletedTask;
        if (card.Data.TrackPaths.Contains(row.Song.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            SetError("La canción ya está en esta playlist.");
            return Task.CompletedTask;
        }
        return SaveAsync(card.Data with { TrackPaths = [.. card.Data.TrackPaths, row.Song.FilePath] });
    }

    public Task RemoveAsync(int index) => EditTracksAsync(index, paths => paths.RemoveAt(index));

    public Task MoveAsync(int index, int offset) => EditTracksAsync(index, paths =>
    {
        var next = index + offset;
        if (next < 0 || next >= paths.Count) return;
        (paths[next], paths[index]) = (paths[index], paths[next]);
    });

    private Task EditTracksAsync(int index, Action<List<string>> edit)
    {
        if (!CanEdit || Selected is not { } card || index < 0 || index >= card.Tracks.Count) return Task.CompletedTask;
        var paths = card.Data.TrackPaths.ToList();
        edit(paths);
        return SaveAsync(card.Data with { TrackPaths = paths.AsReadOnly() });
    }

    private Task SaveAsync(Playlist playlist) => RunAsync(async () =>
    {
        await repository.SaveAsync(playlist, cancellationToken);
        var index = saved.FindIndex(item => item.Id == playlist.Id);
        if (index < 0) saved.Add(playlist);
        else saved[index] = playlist;
        RefreshCards(playlist.Id);
    });

    private void RefreshCards(Guid? id)
    {
        Cards = saved.OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(playlist => new PlaylistCard<TCover>(playlist, playlist.TrackPaths
                .Select(path => new PlaylistTrack(path, byPath.GetValueOrDefault(path))).ToArray(),
                DecodeCustomCover(playlist.Cover)))
            .ToArray();
        Notify(nameof(Cards));
        Open(id);
    }

    private TCover? DecodeCustomCover(byte[]? cover)
    {
        try { return decodeCover(cover, 512); }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private Task RunAsync(Func<Task> operation)
    {
        if (busy || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
        Pending = RunCoreAsync(operation);
        return Pending;
    }

    private async Task RunCoreAsync(Func<Task> operation)
    {
        busy = true;
        Notify(nameof(CanEdit));
        SetError(null);
        try { await operation(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetError("No se pudieron guardar o leer las playlists. Comprueba el acceso a la carpeta de datos.");
        }
        finally
        {
            busy = false;
            Notify(nameof(CanEdit));
        }
    }

    private void SetError(string? value)
    {
        Error = value;
        Notify(nameof(Error));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
