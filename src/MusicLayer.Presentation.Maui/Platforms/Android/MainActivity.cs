using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Insets = AndroidX.Core.Graphics.Insets;
using NativeView = Android.Views.View;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

[Activity(Theme = "@style/Tonic.MainTheme", Icon = "@mipmap/tonic_launcher", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize |
    ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
    ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    private const int PickDocumentRequest = 271;
    private TaskCompletionSource<string[]>? documentSelection;

    internal Task<string[]> PickAudioAsync() =>
        PickDocumentsAsync(["audio/*", "application/flac", "application/x-flac"], allowMultiple: true);

    internal async Task<string?> PickLrcAsync() =>
        (await PickDocumentsAsync(mimeTypes: null, allowMultiple: false)).FirstOrDefault();

    private Task<string[]> PickDocumentsAsync(string[]? mimeTypes, bool allowMultiple)
    {
        if (documentSelection is not null) return Task.FromResult(Array.Empty<string>());
        using var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        if (mimeTypes is not null) intent.PutExtra(Intent.ExtraMimeTypes, mimeTypes);
        intent.PutExtra(Intent.ExtraAllowMultiple, allowMultiple);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        if (allowMultiple) intent.AddFlags(ActivityFlags.GrantPersistableUriPermission);
        var completion = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        documentSelection = completion;
        try { StartActivityForResult(intent, PickDocumentRequest); }
        catch (ActivityNotFoundException error)
        {
            documentSelection = null;
            completion.SetException(new IOException("No hay un selector de archivos disponible.", error));
        }
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickDocumentRequest) return;
        var selection = documentSelection;
        documentSelection = null;
        List<string> files = [];
        if (resultCode == Result.Ok && data is not null)
        {
            if (data.ClipData is { } clip)
            {
                for (int i = 0; i < clip.ItemCount; i++)
                    if (clip.GetItemAt(i)?.Uri?.ToString() is { } uri) files.Add(uri);
            }
            else if (data.Data?.ToString() is { } uri) files.Add(uri);
        }
        selection?.TrySetResult(files.Distinct().ToArray());
    }

    protected override void OnDestroy()
    {
        documentSelection?.TrySetCanceled();
        documentSelection = null;
        base.OnDestroy();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Window is not { } window || FindViewById(global::Android.Resource.Id.Content) is not { } content)
            return;

        WindowCompat.SetDecorFitsSystemWindows(window, false);
        window.NavigationBarContrastEnforced = false;
        content.SetBackgroundResource(Resource.Color.tonic_window_background);
        ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarInsets());
        ViewCompat.RequestApplyInsets(content);
    }

    private sealed class SystemBarInsets : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(NativeView? view, WindowInsetsCompat? insets)
        {
            int mask = WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout();
            if (view is null || insets?.GetInsets(mask) is not { } bars)
                return insets;

            view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            // Both Shell panes share the root padding. Pass the keyboard area still available to MAUI.
            var remaining = new WindowInsetsCompat.Builder(insets);
            remaining.SetInsets(mask, Insets.None!);
            remaining.SetInsetsIgnoringVisibility(mask, Insets.None!);
            remaining.SetDisplayCutout(null);
            int ime = WindowInsetsCompat.Type.Ime();
            if (insets.GetInsets(ime) is { Bottom: > 0 } keyboard)
                remaining.SetInsets(ime, Insets.Of(keyboard.Left, keyboard.Top, keyboard.Right,
                    Math.Max(0, keyboard.Bottom - bars.Bottom))!);

            return remaining.Build() ?? insets;
        }
    }
}
