using Microsoft.Data.Sqlite;
using MusicLayer.Application.Abstractions;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Infrastructure.Persistence;

public sealed class SqlitePlaylistRepository(string? directory = null) : IPlaylistRepository
{
    private readonly string storage = Path.GetFullPath(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicLayer"));

    public Task<IReadOnlyList<Playlist>> ReadAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync<IReadOnlyList<Playlist>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.id, p.name, t.file_path, d.genre, d.description,
                       CASE WHEN t.position IS NULL OR t.position = 0 THEN d.cover END
                FROM playlists p
                LEFT JOIN playlist_tracks t ON t.playlist_id = p.id
                LEFT JOIN playlist_details d ON d.playlist_id = p.id
                ORDER BY p.name COLLATE NOCASE, p.id, t.position
                """;
            using var reader = command.ExecuteReader();
            var playlists = new List<Playlist>();
            List<string>? tracks = null;
            while (reader.Read())
            {
                var id = reader.GetGuid(0);
                if (tracks is null || playlists[^1].Id != id)
                {
                    tracks = [];
                    playlists.Add(new Playlist(id, reader.GetString(1), tracks.AsReadOnly())
                    {
                        Genre = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Cover = reader.IsDBNull(5) ? null : reader.GetFieldValue<byte[]>(5)
                    });
                }
                if (!reader.IsDBNull(2)) tracks.Add(reader.GetString(2));
            }
            return playlists.AsReadOnly();
        }, cancellationToken);

    public Task SaveAsync(Playlist playlist, CancellationToken cancellationToken = default)
        => ExecuteAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO playlists (id, name) VALUES ($id, $name)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name;
                INSERT INTO playlist_details (playlist_id, genre, description, cover)
                VALUES ($id, $genre, $description, $cover)
                ON CONFLICT(playlist_id) DO UPDATE SET genre = excluded.genre,
                    description = excluded.description, cover = excluded.cover;
                DELETE FROM playlist_tracks WHERE playlist_id = $id;
                """;
            command.Parameters.AddWithValue("$id", playlist.Id);
            command.Parameters.AddWithValue("$name", playlist.Name);
            command.Parameters.AddWithValue("$genre", (object?)playlist.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("$description", (object?)playlist.Description ?? DBNull.Value);
            command.Parameters.Add("$cover", SqliteType.Blob).Value = (object?)playlist.Cover ?? DBNull.Value;
            command.ExecuteNonQuery();

            command.CommandText = """
                INSERT INTO playlist_tracks (playlist_id, position, file_path)
                VALUES ($id, $position, $path)
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", playlist.Id);
            var position = command.Parameters.Add("$position", SqliteType.Integer);
            var path = command.Parameters.Add("$path", SqliteType.Text);
            for (var index = 0; index < playlist.TrackPaths.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                position.Value = index;
                path.Value = playlist.TrackPaths[index];
                command.ExecuteNonQuery();
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);

    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> action, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(storage);
                var settings = new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(storage, "preferences.db"),
                    Pooling = false,
                    ForeignKeys = true,
                    DefaultTimeout = 5
                };
                using var connection = new SqliteConnection(settings.ToString());
                connection.Open();
                using var schema = connection.CreateCommand();
                schema.CommandText = """
                    CREATE TABLE IF NOT EXISTS playlists (
                        id TEXT NOT NULL PRIMARY KEY,
                        name TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS playlist_tracks (
                        playlist_id TEXT NOT NULL REFERENCES playlists(id),
                        position INTEGER NOT NULL,
                        file_path TEXT NOT NULL,
                        PRIMARY KEY (playlist_id, position)
                    );
                    CREATE TABLE IF NOT EXISTS playlist_details (
                        playlist_id TEXT NOT NULL PRIMARY KEY REFERENCES playlists(id),
                        genre TEXT,
                        description TEXT,
                        cover BLOB
                    );
                    """;
                schema.ExecuteNonQuery();
                cancellationToken.ThrowIfCancellationRequested();
                return action(connection);
            }
            catch (SqliteException error)
            {
                throw new IOException("Unable to access the playlist database.", error);
            }
        }, cancellationToken);
}
