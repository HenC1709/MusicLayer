using Android.Graphics;

namespace MusicLayer.Presentation.Maui;

internal static class CoverThumbnail
{
    public static byte[]? Create(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, options)?.Dispose();
        if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

        int sample = 1;
        while (Math.Max(options.OutWidth, options.OutHeight) / sample > 256) sample *= 2;
        options.InSampleSize = sample;
        options.InJustDecodeBounds = false;
        using var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
        using var stream = new MemoryStream();
        return bitmap?.Compress(Bitmap.CompressFormat.Png!, 100, stream) == true ? stream.ToArray() : null;
    }
}
