using Microsoft.Data.Sqlite;
using MusicLayer.Application.Abstractions;

namespace MusicLayer.Infrastructure.Persistence;

public sealed class SqlitePlaybackPreferencesRepository : IPlaybackPreferencesRepository
{
    private const int DefaultVolume = 100;
    private readonly string directory;
    private readonly string connectionString;

    public SqlitePlaybackPreferencesRepository(string? directory = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicLayer"));
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(this.directory, "preferences.db"),
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public Task<PlaybackPreferences> ReadAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT library_folder, COALESCE(volume_percent, 100), remember_position, auto_advance,
                       follow_lyrics, dim_inactive_lyrics, track_path, position_ticks
                FROM (SELECT 1 AS id)
                LEFT JOIN playback_preferences USING (id)
                LEFT JOIN player_options USING (id)
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            var options = reader.IsDBNull(2) ? new PlayerOptions() : new PlayerOptions(reader.GetBoolean(2),
                reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5));
            return new PlaybackPreferences(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1))
            {
                Options = options,
                Bookmark = options.RememberPosition && !reader.IsDBNull(6)
                    ? new PlaybackBookmark(reader.GetString(6), TimeSpan.FromTicks(reader.GetInt64(7))) : null
            };
        }, cancellationToken);

    public Task SaveFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return UpdateAsync("""
            INSERT INTO playback_preferences (id, library_folder) VALUES (1, $value)
            ON CONFLICT (id) DO UPDATE SET library_folder = excluded.library_folder
            """, folder, cancellationToken);
    }

    public Task SaveVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volumePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volumePercent, 100);
        return UpdateAsync("""
            INSERT INTO playback_preferences (id, volume_percent) VALUES (1, $value)
            ON CONFLICT (id) DO UPDATE SET volume_percent = excluded.volume_percent
            """, volumePercent, cancellationToken);
    }

    public Task SaveOptionsAsync(PlayerOptions options, CancellationToken cancellationToken = default)
        => ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO player_options (id, remember_position, auto_advance, follow_lyrics, dim_inactive_lyrics)
                VALUES (1, $remember, $advance, $follow, $dim)
                ON CONFLICT (id) DO UPDATE SET remember_position = $remember, auto_advance = $advance,
                    follow_lyrics = $follow, dim_inactive_lyrics = $dim,
                    track_path = CASE WHEN $remember THEN track_path END,
                    position_ticks = CASE WHEN $remember THEN position_ticks ELSE 0 END
                """;
            command.Parameters.AddWithValue("$remember", options.RememberPosition);
            command.Parameters.AddWithValue("$advance", options.AutoAdvance);
            command.Parameters.AddWithValue("$follow", options.FollowLyrics);
            command.Parameters.AddWithValue("$dim", options.DimInactiveLyrics);
            return command.ExecuteNonQuery();
        }, cancellationToken);

    public Task SaveBookmarkAsync(PlaybackBookmark? bookmark, CancellationToken cancellationToken = default)
        => ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE player_options SET track_path = $path, position_ticks = $ticks
                WHERE id = 1 AND remember_position = 1
                """;
            command.Parameters.AddWithValue("$path", (object?)bookmark?.FilePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$ticks", bookmark?.Position.Ticks ?? 0);
            return command.ExecuteNonQuery();
        }, cancellationToken);

    public Task<IReadOnlyList<string>> ReadFavoritesAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync<IReadOnlyList<string>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT file_path FROM favorite_tracks";
            using var reader = command.ExecuteReader();
            var paths = new List<string>();
            while (reader.Read()) paths.Add(reader.GetString(0));
            return paths;
        }, cancellationToken);

    public Task SaveFavoriteAsync(string path, bool isFavorite, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = isFavorite
                ? "INSERT INTO favorite_tracks (path_key, file_path) VALUES ($key, $path) ON CONFLICT(path_key) DO NOTHING"
                : "DELETE FROM favorite_tracks WHERE path_key = $key";
            command.Parameters.AddWithValue("$key", path.ToUpperInvariant());
            if (isFavorite) command.Parameters.AddWithValue("$path", path);
            return command.ExecuteNonQuery();
        }, cancellationToken);
    }

    private Task UpdateAsync(string sql, object value, CancellationToken cancellationToken)
        => ExecuteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$value", value);
            return command.ExecuteNonQuery();
        }, cancellationToken);

    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> operation, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS playback_preferences (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        library_folder TEXT,
                        volume_percent INTEGER NOT NULL DEFAULT {DefaultVolume} CHECK (volume_percent BETWEEN 0 AND 100)
                    );
                    CREATE TABLE IF NOT EXISTS player_options (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        remember_position INTEGER NOT NULL,
                        auto_advance INTEGER NOT NULL,
                        follow_lyrics INTEGER NOT NULL,
                        dim_inactive_lyrics INTEGER NOT NULL,
                        track_path TEXT,
                        position_ticks INTEGER NOT NULL DEFAULT 0 CHECK (position_ticks >= 0)
                    );
                    CREATE TABLE IF NOT EXISTS favorite_tracks (
                        path_key TEXT NOT NULL PRIMARY KEY,
                        file_path TEXT NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
                cancellationToken.ThrowIfCancellationRequested();
                return operation(connection);
            }
            catch (SqliteException error)
            {
                throw new IOException("Unable to access the playback preferences database.", error);
            }
        }, cancellationToken);
}
