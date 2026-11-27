using System.Security.Cryptography;
using Android.Graphics;
using MusicLayer.Shared.Metadata;
using Path = System.IO.Path;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

internal static class CoverThumbnail
{
    private const int MaxSize = 120;
    private static string DirectoryPath => Path.Combine(FileSystem.CacheDirectory, "library-thumbnails-120");
    private static string DetailDirectory => Path.Combine(FileSystem.CacheDirectory, "playback-artwork-768");
    private static readonly SemaphoreSlim DetailReader = new(1, 1);

    public static string? GetPath(string? fileName)
    {
        if (fileName is not null)
        {
            string path = Path.Combine(DirectoryPath, Path.GetFileName(fileName));
            if (File.Exists(path)) return path;
            // Upgrade thumbnails from an already saved library without opening the FLAC again.
            string old = Path.Combine(FileSystem.CacheDirectory, "library-thumbnails", Path.GetFileName(fileName));
            try
            {
                if (File.Exists(old) && Save(File.ReadAllBytes(old), DirectoryPath, MaxSize, 82, Path.GetFileName(fileName)) is not null)
                    return path;
            }
            catch (IOException) { }
        }

        return null;
    }

    public static string? Save(byte[]? data) => Save(data, DirectoryPath, MaxSize, 82);

    public static async Task<string?> GetPlaybackArtworkAsync(LibraryTrack track, TagLibTrackMetadataReader metadata,
        CancellationToken cancellationToken)
    {
        if (track.ContentUri is not { } contentUri) return null;
        string? fileName = track.ArtworkPath is { } thumbnail ? Path.GetFileName(thumbnail) : null;
        string? cached = fileName is null ? null : Path.Combine(DetailDirectory, fileName);
        if (cached is not null && File.Exists(cached)) return cached;

        await DetailReader.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cached is not null && File.Exists(cached)) return cached;
            using var uri = global::Android.Net.Uri.Parse(contentUri);
            if (uri is null) return null;
            await using var input = AudioDocuments.Open(uri);
            var cover = await AudioDocuments.ReadSeekableAsync(input,
                stream => metadata.ReadSelection(Path.GetFileName(track.Row.Song.FilePath), stream).Cover,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var saved = Save(cover, DetailDirectory, 768, 92, fileName);
            return saved is null ? null : Path.Combine(DetailDirectory, saved);
        }
        catch (Exception error) when (error is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                      or IOException or UnauthorizedAccessException or Java.IO.IOException
                                      or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            return null;
        }
        finally { DetailReader.Release(); }
    }

    private static string? Save(byte[]? data, string directory, int maxSize, int quality, string? existingName = null)
    {
        if (data is not { Length: > 0 }) return null;

        try
        {
            Directory.CreateDirectory(directory);
            string fileName = existingName ?? $"{Convert.ToHexString(SHA256.HashData(data))}.jpg";
            string path = Path.Combine(directory, fileName);
            if (File.Exists(path)) return fileName;

            using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(data, 0, data.Length, options)?.Dispose();
            int largestSide = Math.Max(options.OutWidth, options.OutHeight);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

            int sample = 1;
            while (largestSide / sample > 2 * maxSize) sample *= 2;
            options.InSampleSize = sample;
            options.InJustDecodeBounds = false;

            using var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
            if (bitmap is null) return null;

            double scale = (double)maxSize / Math.Max(bitmap.Width, bitmap.Height);
            using var resized = scale >= 1 ? null : Bitmap.CreateScaledBitmap(bitmap,
                Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true);
            using var output = new MemoryStream();
            if (!(resized ?? bitmap).Compress(Bitmap.CompressFormat.Jpeg!, quality, output)) return null;

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, output.ToArray());
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally { File.Delete(temporaryPath); }

            return fileName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Java.Lang.IllegalArgumentException)
        {
            return null;
        }
    }
}
