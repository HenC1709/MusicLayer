using Android.Graphics;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

// 256 decoded 120px rounded ARGB thumbnails occupy at most about 14 MiB.
internal static class LibraryCoverCache
{
    public const int Capacity = 256;
    private static readonly Dictionary<string, LinkedListNode<(string Path, Bitmap Image)>> entries = new();
    private static readonly LinkedList<(string Path, Bitmap Image)> recent = new();

    public static Bitmap? Find(string path)
    {
        lock (entries)
        {
            if (!entries.TryGetValue(path, out var node)) return null;
            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Image;
        }
    }

    public static Bitmap? Load(string path)
    {
        if (Find(path) is { } cached) return cached;
        using var decoded = BitmapFactory.DecodeFile(path);
        if (decoded is null) return null;

        var image = CreateRoundedSquare(decoded);
        lock (entries)
        {
            if (entries.TryGetValue(path, out var existing))
            {
                image.Dispose();
                return existing.Value.Image;
            }
            entries[path] = recent.AddFirst((path, image));
            if (entries.Count > Capacity)
            {
                entries.Remove(recent.Last!.Value.Path);
                recent.RemoveLast(); // A visible row may still reference the evicted bitmap.
            }
        }
        return image;
    }

    private static Bitmap CreateRoundedSquare(Bitmap raw)
    {
        int size = Math.Min(raw.Width, raw.Height);
        int x = (raw.Width - size) / 2;
        int y = (raw.Height - size) / 2;

        var output = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(output);
        using var shader = new BitmapShader(raw, Shader.TileMode.Clamp!, Shader.TileMode.Clamp!);
        if (x != 0 || y != 0)
        {
            using var matrix = new Matrix();
            matrix.SetTranslate(-x, -y);
            shader.SetLocalMatrix(matrix);
        }

        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
        paint.SetShader(shader);
        float radius = size * (6f / 44f);
        using var bounds = new RectF(0, 0, size, size);
        canvas.DrawRoundRect(bounds, radius, radius, paint);
        output.PrepareToDraw();
        return output;
    }
}
