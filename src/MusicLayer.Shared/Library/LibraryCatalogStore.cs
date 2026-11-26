using System.Text.Json;
using MusicLayer.Domain.Entities;

namespace MusicLayer.Shared.Library;

public sealed record LibraryCatalogTrack(string FileName, TrackMetadata? Metadata, string? CoverFileName);

public sealed class LibraryCatalogStore(string filePath)
{
    public async Task<LibraryCatalogTrack[]> LoadAsync()
    {
        FileStream input;
        try { input = File.OpenRead(filePath); }
        catch (FileNotFoundException) { return []; }

        await using (input)
            return await JsonSerializer.DeserializeAsync<LibraryCatalogTrack[]>(input).ConfigureAwait(false)
                ?? throw new JsonException("The library catalog is empty.");
    }

    public async Task SaveAsync(IReadOnlyList<LibraryCatalogTrack> tracks)
    {
        string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 16 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(output, tracks).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally { File.Delete(temporaryPath); }
    }
}
