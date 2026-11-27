using System.Security.Cryptography;
using System.Text;
using MusicLayer.Application.Abstractions;
using MusicLayer.Application.UseCases.Lyrics;
using Uri = Android.Net.Uri;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

public sealed class LocalLrcStore : ILocalLyricsReader
{
    private const int MaxCharacters = 256 * 1024;
    private readonly string directory = Path.Combine(FileSystem.AppDataDirectory, "local-lyrics");

    public async Task<string?> ReadAsync(string audioKey, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(GetPath(audioKey), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal async Task ImportAsync(string audioKey, string document)
    {
        using var uri = Uri.Parse(document) ?? throw new IOException("No se pudo abrir el archivo.");
        if (!AudioDocuments.GetName(uri).EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Elegí un archivo con la extensión .lrc.");

        var resolver = global::Android.App.Application.Context.ContentResolver!;
        using var stream = resolver.OpenInputStream(uri) ?? throw new IOException("No se pudo abrir el archivo.");
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxCharacters + 1];
        int length = await reader.ReadBlockAsync(buffer.AsMemory()).ConfigureAwait(false);
        if (length > MaxCharacters)
            throw new InvalidDataException("El archivo .lrc es demasiado grande.");

        string text = new(buffer, 0, length);
        if (!LrcParser.Parse(text).Any(line => line.Text.Length > 0))
            throw new InvalidDataException("El archivo .lrc debe contener versos con marcas de tiempo.");

        Directory.CreateDirectory(directory);
        string path = GetPath(audioKey);
        string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(staging, text).ConfigureAwait(false);
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string GetPath(string audioKey) =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(audioKey))) + ".lrc");
}
