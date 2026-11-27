using Android.Content;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Uri = Android.Net.Uri;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

internal static class AudioDocuments
{
    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    internal static string GetName(Uri uri)
    {
        using var row = Resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        return row?.MoveToFirst() == true ? Path.GetFileName(row.GetString(0)) ?? "" : "";
    }

    internal static Stream Open(Uri uri)
    {
        using var descriptor = Resolver.OpenFileDescriptor(uri, "r")
            ?? throw new IOException("El archivo ya no está disponible.");
        var handle = new SafeFileHandle((IntPtr)descriptor.DetachFd(), ownsHandle: true);
        try { return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    internal static async Task<T> ReadSeekableAsync<T>(Stream source, Func<Stream, T> read,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SupportsRandomAccess(source)) return read(source);

        string path = Path.Combine(FileSystem.CacheDirectory, $"audio-{Guid.NewGuid():N}.tmp");
        await using var temporary = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await source.CopyToAsync(temporary, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return read(temporary);
    }

    private static bool SupportsRandomAccess(Stream stream)
    {
        if (!stream.CanSeek) return false;
        try { _ = stream.Length; return true; }
        catch (NotSupportedException) { return false; }
    }

    internal static void Keep(Uri uri) => Resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);

    internal static void KeepOnly(IEnumerable<string?> retained)
    {
        var keep = retained.OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var permission in Resolver.PersistedUriPermissions ?? [])
        {
            if (permission.Uri is not { } uri || uri.ToString() is not { } id || keep.Contains(id)) continue;
            try { Resolver.ReleasePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission); }
            catch (Java.Lang.SecurityException) { }
        }
    }
}
