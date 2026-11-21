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
            command.CommandText = "SELECT library_folder, volume_percent FROM playback_preferences WHERE id = 1";
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new PlaybackPreferences(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1))
                : new PlaybackPreferences(null, DefaultVolume);
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
                    )
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
