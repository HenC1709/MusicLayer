using Android.Content;
using Android.Media;
using Uri = Android.Net.Uri;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

public sealed class AndroidPlayback
{
    private readonly Context context = global::Android.App.Application.Context;
    private MediaPlayer? player;
    private AudioFocusRequestClass? focus;
    private PlaybackService? service;
    private bool ready;
    private bool preparing;
    private bool resumeAfterInterruption;
    private bool playWhenReady;
    private int index = -1;

    public event EventHandler? Changed;
    public IReadOnlyList<LibraryTrack> Queue { get; private set; } = [];
    public LibraryTrack? Current => index >= 0 && index < Queue.Count ? Queue[index] : null;
    public bool IsPlaying => ready && player?.IsPlaying == true;
    public bool IsPreparing => preparing;
    public int Position => ready ? player?.CurrentPosition ?? 0 : 0;
    public int Duration => ready ? Math.Max(0, player?.Duration ?? 0) : 0;
    public bool Shuffle { get; private set; }
    public int Repeat { get; private set; }
    public string? Error { get; private set; }

    public void Play(IReadOnlyList<LibraryTrack> tracks, LibraryTrack track)
    {
        if (track.ContentUri is null)
        {
            Error = "Volvé a importar esta canción para autorizar la reproducción.";
            Notify();
            return;
        }

        Queue = tracks.Where(item => item.ContentUri is not null).ToArray();
        PlayAt(Array.IndexOf((LibraryTrack[])Queue, track));
    }

    public void PlayQueued(LibraryTrack track)
    {
        for (int i = 0; i < Queue.Count; i++)
        {
            if (ReferenceEquals(Queue[i], track))
            {
                PlayAt(i);
                return;
            }
        }
    }

    public void Toggle()
    {
        if (IsPlaying || preparing) Pause();
        else Resume();
    }

    public void Resume()
    {
        if (Current is null) return;
        if (!ready && !preparing) { PlayAt(index); return; }
        playWhenReady = true;
        if (ready && RequestFocus()) player!.Start();
        Notify();
    }

    public void Pause()
    {
        resumeAfterInterruption = false;
        playWhenReady = false;
        if (IsPlaying) player!.Pause();
        ReleaseFocus();
        Notify();
    }

    public void Seek(int milliseconds)
    {
        if (ready) player!.SeekTo(Math.Clamp(milliseconds, 0, Duration), MediaPlayerSeekMode.ClosestSync);
        Notify();
    }

    public void Previous()
    {
        if (Position > 3000) Seek(0);
        else if (index > 0) PlayAt(index - 1);
        else if (index == 0) Seek(0);
    }

    public void Next() => Advance(completed: false);
    public void ToggleShuffle() { Shuffle = !Shuffle; Notify(); }
    public void CycleRepeat() { Repeat = (Repeat + 1) % 3; Notify(); }

    private void Advance(bool completed)
    {
        if (Current is null) return;
        if (completed && Repeat == 2) { PlayAt(index); return; }
        if (Shuffle && Queue.Count > 1)
        {
            int next = Random.Shared.Next(Queue.Count - 1);
            PlayAt(next >= index ? next + 1 : next);
        }
        else if (index + 1 < Queue.Count) PlayAt(index + 1);
        else if (Repeat == 1) PlayAt(0);
        else if (completed) Stop();
    }

    private void PlayAt(int position)
    {
        if ((uint)position >= (uint)Queue.Count) return;
        ReleasePlayer();
        index = position;
        Error = null;
        playWhenReady = preparing = true;
        Notify();

        if (service is not null) Prepare();
        else
        {
            try
            {
                using var intent = new Intent(context, typeof(PlaybackService));
                context.StartForegroundService(intent);
            }
            catch (Java.Lang.IllegalStateException) { Fail("Abrí Tonic para iniciar la reproducción."); }
            catch (Java.Lang.SecurityException) { Fail("Android no permitió iniciar la reproducción."); }
        }
    }

    internal void Attach(PlaybackService owner)
    {
        service = owner;
        if (preparing && player is null) Prepare();
        else Notify();
    }

    internal void Detach(PlaybackService owner)
    {
        if (service != owner) return;
        service = null;
        ReleasePlayer();
        ReleaseFocus();
        Notify();
    }

    private void Prepare()
    {
        try
        {
            using var uri = Uri.Parse(Current!.ContentUri!);
            var media = new MediaPlayer();
            player = media;
            using var attributes = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build();
            media.SetAudioAttributes(attributes);
            media.SetWakeMode(context, global::Android.OS.WakeLockFlags.Partial);
            media.Prepared += (_, _) =>
            {
                if (player != media) return;
                ready = true;
                preparing = false;
                if (playWhenReady && RequestFocus()) media.Start();
                Notify();
            };
            media.Completion += (_, _) => { if (player == media) Advance(completed: true); };
            media.Error += (_, args) =>
            {
                args.Handled = true;
                if (player == media) Fail("No se pudo reproducir el archivo seleccionado.");
            };
            media.SetDataSource(context, uri!);
            media.PrepareAsync();
        }
        catch (Exception error) when (error is Java.IO.IOException or Java.Lang.IllegalArgumentException
                                      or Java.Lang.SecurityException or Java.Lang.IllegalStateException)
        {
            Fail("El archivo ya no está disponible. Seleccionalo de nuevo en Biblioteca.");
        }
    }

    private bool RequestFocus()
    {
        if (focus is not null) return true;
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        // Keep the listener alive for the full request, including temporary interruptions.
        focusListener = new FocusListener(this);
        using var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build();
        focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(attributes!)!.SetOnAudioFocusChangeListener(focusListener)!.Build();
        if (audio.RequestAudioFocus(focus!) == AudioFocusRequest.Granted) return true;
        ReleaseFocus();
        Error = "Otra aplicación está usando el audio.";
        return false;
    }

    private FocusListener? focusListener;

    private void ReleaseFocus()
    {
        if (focus is not null)
        {
            var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
            audio.AbandonAudioFocusRequest(focus);
            focus.Dispose();
            focus = null;
        }
        focusListener?.Dispose();
        focusListener = null;
    }

    private sealed class FocusListener(AndroidPlayback owner) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus change)
        {
            if (change == AudioFocus.Gain)
            {
                if (owner.ready) owner.player?.SetVolume(1, 1);
                if (owner.resumeAfterInterruption && owner.ready) owner.player?.Start();
                owner.resumeAfterInterruption = false;
                owner.Notify();
            }
            else if (change == AudioFocus.LossTransientCanDuck && owner.ready) owner.player?.SetVolume(.2f, .2f);
            else if (change == AudioFocus.LossTransient)
            {
                owner.resumeAfterInterruption = owner.IsPlaying;
                if (owner.IsPlaying) owner.player?.Pause();
                owner.Notify();
            }
            else if (change == AudioFocus.Loss) owner.Pause();
        }
    }

    private void ReleasePlayer()
    {
        ready = preparing = resumeAfterInterruption = false;
        var old = player;
        player = null;
        old?.Release();
        old?.Dispose();
    }

    private void Fail(string message)
    {
        Error = message;
        Stop();
    }

    public void Stop()
    {
        ReleasePlayer();
        ReleaseFocus();
        var owner = service;
        service = null;
        owner?.FinishPlayback();
        Notify();
    }

    private void Notify()
    {
        service?.RefreshNotification();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
