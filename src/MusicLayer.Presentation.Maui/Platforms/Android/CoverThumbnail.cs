using System.Security.Cryptography;
using Android.Graphics;
using Path = System.IO.Path;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

internal static class CoverThumbnail
{
    private const int MaxSize = 120;
    private static string DirectoryPath => Path.Combine(FileSystem.CacheDirectory, "library-thumbnails-120");

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
                if (File.Exists(old) && Save(File.ReadAllBytes(old), Path.GetFileName(fileName)) is not null)
                    return path;
            }
            catch (IOException) { }
        }

        return null;
    }

    public static string? Save(byte[]? data) => Save(data, null);

    private static string? Save(byte[]? data, string? existingName)
    {
        if (data is not { Length: > 0 }) return null;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string fileName = existingName ?? $"{Convert.ToHexString(SHA256.HashData(data))}.jpg";
            string path = Path.Combine(DirectoryPath, fileName);
            if (File.Exists(path)) return fileName;

            using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(data, 0, data.Length, options)?.Dispose();
            int largestSide = Math.Max(options.OutWidth, options.OutHeight);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

            int sample = 1;
            while (largestSide / sample > 2 * MaxSize) sample *= 2;
            options.InSampleSize = sample;
            options.InJustDecodeBounds = false;

            using var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
            if (bitmap is null) return null;

            double scale = (double)MaxSize / Math.Max(bitmap.Width, bitmap.Height);
            using var resized = scale >= 1 ? null : Bitmap.CreateScaledBitmap(bitmap,
                Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true);
            using var output = new MemoryStream();
            if (!(resized ?? bitmap).Compress(Bitmap.CompressFormat.Jpeg!, 82, output)) return null;

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
