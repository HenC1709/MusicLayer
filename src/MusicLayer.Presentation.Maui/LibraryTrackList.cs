namespace MusicLayer.Presentation.Maui;

public sealed class LibraryTrackList : View
{
    public event EventHandler<LibraryTrack>? TrackTapped;
    internal void Tap(LibraryTrack track) => TrackTapped?.Invoke(this, track);

    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items), typeof(IReadOnlyList<LibraryTrack>), typeof(LibraryTrackList), Array.Empty<LibraryTrack>());

    public IReadOnlyList<LibraryTrack> Items
    {
        get => (IReadOnlyList<LibraryTrack>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty ActiveTrackProperty = BindableProperty.Create(
        nameof(ActiveTrack), typeof(LibraryTrack), typeof(LibraryTrackList));

    public LibraryTrack? ActiveTrack
    {
        get => (LibraryTrack?)GetValue(ActiveTrackProperty);
        set => SetValue(ActiveTrackProperty, value);
    }
}
