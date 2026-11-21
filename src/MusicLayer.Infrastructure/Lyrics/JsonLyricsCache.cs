using System.Security.Cryptography;
using System.Text.Json;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;

namespace MusicLayer.Infrastructure.Lyrics;

public sealed class JsonLyricsCache(string directory) : ILyricsCache
{
    public Task<LyricsRecord?> ReadAsync(LyricsCacheKey key, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            try
            {
                await using var stream = new FileStream(GetPath(key), FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);
                return await JsonSerializer.DeserializeAsync<LyricsRecord>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }, cancellationToken);

    public Task WriteAsync(LyricsCacheKey key, LyricsRecord record, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            var path = GetPath(key);
            var stagingPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                await using (var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, record, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(stagingPath, path, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A cache write failure does not discard the lyrics already obtained.
            }
            finally
            {
                try { File.Delete(stagingPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }, cancellationToken);

    private string GetPath(LyricsCacheKey key)
    {
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(key));
        return Path.Combine(directory, $"v1-{Convert.ToHexString(hash)}.json");
    }
}
