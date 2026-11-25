using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicLayer.Presentation.Wpf.ViewModels;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class PlaylistsView : UserControl
{
    private readonly SongThumbnailLoader thumbnails = new();
    private PlaylistsViewModel<BitmapSource>? Model => DataContext as PlaylistsViewModel<BitmapSource>;
    private MainViewModel? Player => Window.GetWindow(this)?.DataContext as MainViewModel;

    public PlaylistsView()
    {
        InitializeComponent();
        UpdateTrackActions();
    }

    private void CreatePlaylist(object sender, RoutedEventArgs e) => EditDetails(null);

    private void EditPlaylist(object sender, RoutedEventArgs e)
    {
        if (Model?.Selected is { } playlist) EditDetails(playlist);
    }

    private void EditDetails(PlaylistCard<BitmapSource>? playlist)
    {
        if (Model is not { CanEdit: true } model || Window.GetWindow(this) is not { } owner) return;
        var editor = new PlaylistEditor(owner, model, playlist);
        if (editor.ShowDialog() != true) return;

        CreationFeedback.Visibility = Visibility.Collapsed;
        if (playlist is null && model.Selected is { } created)
        {
            CreationMessage.Text = $"✓  Playlist «{created.Name}» creada.";
            CreationFeedback.Visibility = Visibility.Visible;
            UIElementAutomationPeer.CreatePeerForElement(CreationMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        FocusCurrentView();
    }

    private void FocusCurrentView()
    {
        if (Model?.Selected is null)
            (Model?.Cards.Count == 0 ? EmptyCreateButton : CreateButton).Focus();
        else SongPicker.Focus();
    }

    private void OpenPlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistCard<BitmapSource> card } || Model is not { } model) return;
        CreationFeedback.Visibility = Visibility.Collapsed;
        model.Open(card.Id);
        FocusCurrentView();
    }

    private void BackToPlaylists(object sender, RoutedEventArgs e)
    {
        Model?.Open(null);
        CreationFeedback.Visibility = Visibility.Collapsed;
        FocusCurrentView();
    }

    private async void AddTrack(object sender, RoutedEventArgs e)
    {
        if (SongPicker.SelectedItem is SongRow row && Model is { } model)
        {
            await model.AddAsync(row);
            if (model.Error is null) SongPicker.SelectedIndex = -1;
        }
    }

    private async void RemoveTrack(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.RemoveAsync(PlaylistTracks.SelectedIndex);
    }

    private async void MoveUp(object sender, RoutedEventArgs e) => await MoveTrackAsync(-1);
    private async void MoveDown(object sender, RoutedEventArgs e) => await MoveTrackAsync(1);

    private async Task MoveTrackAsync(int offset)
    {
        if (Model is not { } model) return;
        var index = PlaylistTracks.SelectedIndex;
        await model.MoveAsync(index, offset);
        if (model.Error is null && index >= 0)
            PlaylistTracks.SelectedIndex = index + offset;
    }

    private void TrackSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTrackActions();

    private void UpdateTrackActions()
    {
        if (PlaylistTracks is null || AddTrackButton is null || SongPicker is null) return;
        AddTrackButton.IsEnabled = SongPicker.SelectedItem is SongRow;
        var index = PlaylistTracks.SelectedIndex;
        RemoveTrackButton.IsEnabled = index >= 0;
        MoveTrackUp.IsEnabled = index > 0;
        MoveTrackDown.IsEnabled = index >= 0 && index + 1 < PlaylistTracks.Items.Count;
    }

    private async void PlayPlaylist(object sender, RoutedEventArgs e) => await PlayAsync(null);

    private async void PlayTrackFromMouse(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(PlaylistTracks, source) is ListBoxItem { Content: PlaylistTrack { Row: { } row } })
            await PlayAsync(row);
    }

    private async void PlayTrackFromKeyboard(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PlaylistTracks.SelectedItem is not PlaylistTrack { Row: { } row }) return;
        e.Handled = true;
        await PlayAsync(row);
    }

    private Task PlayAsync(SongRow? first)
    {
        if (Model?.Selected is not { } card || Player is not { } player) return Task.CompletedTask;
        var rows = card.Tracks.Select(track => track.Row).OfType<SongRow>().ToArray();
        var index = first is null ? 0 : Array.IndexOf(rows, first);
        return player.PlayPlaylistAsync(rows, index);
    }

    private void LoadCardArt(object sender, RoutedEventArgs e) => RefreshThumbnail((Image)sender);
    private void UnloadCardArt(object sender, RoutedEventArgs e) => SongThumbnailLoader.Reset((Image)sender);
    private void ChangeCardArt(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Image { IsLoaded: true } image) RefreshThumbnail(image);
    }
    private void RefreshThumbnail(Image image) => thumbnails.Refresh(image, Player, (ImageSource)Resources["LibraryArtwork"]);
}
