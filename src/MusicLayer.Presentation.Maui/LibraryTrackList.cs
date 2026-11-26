namespace MusicLayer.Presentation.Maui;

public sealed class LibraryTrackList : View
{
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items), typeof(IReadOnlyList<LibraryTrack>), typeof(LibraryTrackList), Array.Empty<LibraryTrack>());

    public IReadOnlyList<LibraryTrack> Items
    {
        get => (IReadOnlyList<LibraryTrack>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
