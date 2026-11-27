using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Session;
using Android.OS;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    private const int NotificationId = 23;
    private const string ChannelId = "tonic-playback";
    private AndroidPlayback playback = null!;
    private MediaSession session = null!;
    private Transport transport = null!;
    private HeadphonesReceiver headphones = null!;
    private NotificationManager notifications = null!;
    private LibraryTrack? publishedTrack;
    private int publishedDuration = -1;
    private bool foreground;

    public override void OnCreate()
    {
        base.OnCreate();
        playback = IPlatformApplication.Current!.Services.GetRequiredService<AndroidPlayback>();
        notifications = (NotificationManager)GetSystemService(NotificationService)!;
        using var channel = new NotificationChannel(ChannelId, "Reproducción de Tonic", NotificationImportance.Low);
        notifications.CreateNotificationChannel(channel);
        session = new MediaSession(this, "Tonic");
        transport = new Transport(playback);
        session.SetCallback(transport);
        session.Active = true;
        headphones = new HeadphonesReceiver(playback);
        using var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        RegisterReceiver(headphones, filter, ReceiverFlags.NotExported);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (playback.Current is null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (!foreground)
        {
            using var notification = BuildNotification();
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            foreground = true;
            playback.Attach(this);
        }

        switch (intent?.Action)
        {
            case "tonic.previous": playback.Previous(); break;
            case "tonic.toggle": playback.Toggle(); break;
            case "tonic.next": playback.Next(); break;
        }
        return StartCommandResult.NotSticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    internal void RefreshNotification()
    {
        if (!foreground) return;
        var track = playback.Current;
        int duration = playback.Duration;
        if (track is not null && (!ReferenceEquals(track, publishedTrack) || publishedDuration != duration))
        {
            using var metadata = new MediaMetadata.Builder();
            metadata.PutString(MediaMetadata.MetadataKeyTitle, track.Title);
            metadata.PutString(MediaMetadata.MetadataKeyArtist, track.Row.Song.Metadata?.Artist ?? "");
            metadata.PutString(MediaMetadata.MetadataKeyAlbum, track.Row.Song.Metadata?.Album ?? "");
            metadata.PutLong(MediaMetadata.MetadataKeyDuration, duration);
            if (track.ArtworkPath is { } path && LibraryCoverCache.Find(path) is { } cover)
                metadata.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, cover);
            using var value = metadata.Build();
            session.SetMetadata(value);
            publishedTrack = track;
            publishedDuration = duration;
        }

        var state = playback.IsPlaying ? PlaybackStateCode.Playing
            : playback.IsPreparing ? PlaybackStateCode.Buffering : PlaybackStateCode.Paused;
        using var builder = new PlaybackState.Builder();
        builder.SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause
            | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSkipToNext | PlaybackState.ActionSeekTo | PlaybackState.ActionStop);
        builder.SetState(state, playback.Position, playback.IsPlaying ? 1 : 0);
        using var playbackState = builder.Build();
        session.SetPlaybackState(playbackState);
        using var notification = BuildNotification();
        notifications.Notify(NotificationId, notification);
    }

    private Notification BuildNotification()
    {
        using var builder = new Notification.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.sixteenth_notes);
        builder.SetContentTitle(playback.Current?.Title ?? "Tonic");
        builder.SetContentText(playback.Current?.Credits ?? "");
        builder.SetOnlyAlertOnce(true);
        builder.SetOngoing(playback.IsPlaying || playback.IsPreparing);
        using var open = new Intent(this, typeof(MainActivity));
        open.AddFlags(ActivityFlags.SingleTop);
        using var openApp = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        builder.SetContentIntent(openApp);
        AddAction(builder, "tonic.previous", "Anterior", global::Android.Resource.Drawable.IcMediaPrevious, 1);
        AddAction(builder, "tonic.toggle", playback.IsPlaying ? "Pausar" : "Reproducir",
            playback.IsPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay, 2);
        AddAction(builder, "tonic.next", "Siguiente", global::Android.Resource.Drawable.IcMediaNext, 3);
        using var style = new Notification.MediaStyle();
        style.SetMediaSession(session.SessionToken);
        style.SetShowActionsInCompactView(0, 1, 2);
        builder.SetStyle(style);
        return builder.Build()!;
    }

    private void AddAction(Notification.Builder builder, string action, string label, int icon, int request)
    {
        using var intent = new Intent(this, typeof(PlaybackService));
        intent.SetAction(action);
        using var pending = PendingIntent.GetService(this, request, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var iconResource = global::Android.Graphics.Drawables.Icon.CreateWithResource(this, icon);
        using var actionBuilder = new Notification.Action.Builder(iconResource, label, pending);
        using var result = actionBuilder.Build();
        builder.AddAction(result);
    }

    internal void FinishPlayback()
    {
        foreground = false;
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnDestroy()
    {
        foreground = false;
        playback.Detach(this);
        UnregisterReceiver(headphones);
        session.Release();
        headphones.Dispose();
        transport.Dispose();
        session.Dispose();
        base.OnDestroy();
    }

    private sealed class Transport(AndroidPlayback player) : MediaSession.Callback
    {
        public override void OnPlay() => player.Resume();
        public override void OnPause() => player.Pause();
        public override void OnStop() => player.Stop();
        public override void OnSkipToNext() => player.Next();
        public override void OnSkipToPrevious() => player.Previous();
        public override void OnSeekTo(long pos) => player.Seek((int)Math.Clamp(pos, 0, int.MaxValue));
    }

    private sealed class HeadphonesReceiver(AndroidPlayback player) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy) player.Pause();
        }
    }
}
