using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicLayer.Presentation.Wpf.ViewModels;
using MusicLayer.Shared.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class NowPlayingView : UserControl
{
    private readonly SongThumbnailLoader thumbnails = new();
    private bool hasOpened;
    private MainViewModel? Model => DataContext as MainViewModel;

    public event RoutedEventHandler? ImportRequested;
    public event RoutedEventHandler? LyricsRequested;

    public NowPlayingView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible || hasOpened) return;
            hasOpened = true;
            var active = Model?.CurrentQueueIndex ?? -1;
            if (active >= 0 && active < QueueList.Items.Count)
                QueueList.ScrollIntoView(QueueList.Items[active]);
            else
                RevealSelection();
        };
        QueueList.SelectionChanged += (_, _) => RevealSelection();
    }

    private void RevealSelection()
    {
        if (IsVisible && QueueList.SelectedItem is { } selected) QueueList.ScrollIntoView(selected);
    }

    private void ResizeView(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        var compact = width < 760;
        var margin = compact ? 12 : Math.Clamp(width * .03, 20, 42);
        var gap = compact ? 12 : 24;
        var queue = Math.Clamp(width * .32, compact ? 246 : 270, 430);
        var topGap = compact ? 0 : Math.Clamp(height * .075, 17, 66);
        var art = compact
            ? Math.Clamp(height - 220, 68, 220)
            : Math.Clamp((width - margin * 2 - gap - queue) * .51, 165, 420);
        art = Math.Min(art, Math.Max(68, height - topGap - 96));

        Layout.Margin = new Thickness(margin, compact ? 10 : 22, margin, compact ? 6 : 14);
        Header.Margin = new Thickness(0, 0, 0, compact ? 8 : 19);
        Heading.FontSize = compact ? 20 : 26;
        Gutter.Width = new GridLength(gap);
        QueueColumn.Width = new GridLength(queue);
        QueuePanel.Margin = new Thickness(0, topGap, 0, compact ? 0 : topGap * .7);
        HeroScroll.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        HeroScroll.Margin = new Thickness(0, topGap, 0, 0);
        var waveformSpace = height - topGap - art - 108;
        WaveformPanel.Visibility = !compact && waveformSpace >= 78 ? Visibility.Visible : Visibility.Collapsed;
        WaveformPanel.Height = Math.Clamp(waveformSpace, 78, 148);
        DockPanel.SetDock(CoverFrame, compact ? Dock.Top : Dock.Left);
        CoverFrame.Width = CoverFrame.Height = art;
        CoverFrame.CornerRadius = new CornerRadius(compact ? 12 : 22);
        CoverFrame.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 22, 0);
        TrackTitle.FontSize = compact ? 22 : Math.Clamp(width * .037, 27, 48);
        TrackTitle.MaxHeight = Math.Ceiling(TrackTitle.FontSize * 2.8);
        Artist.FontSize = compact ? 14 : 19;
        PrimaryAction.MinWidth = compact ? 0 : 104;
        Actions.Margin = new Thickness(0, compact ? 6 : 14, 0, 0);
    }

    private void ImportFolder(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, e);
    private void OpenLyrics(object sender, RoutedEventArgs e) => LyricsRequested?.Invoke(this, e);

    private async void TogglePlayback(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.TogglePlaybackAsync();
    }

    private async Task PlayRowAsync(SongRow? row)
    {
        if (row is null || Model is not { IsReady: true } model) return;
        await model.PlayQueueRowAsync(row);
    }

    private async void PlaySelection(object sender, RoutedEventArgs e) => await PlayRowAsync(QueueList.SelectedItem as SongRow);

    private async void ActivateRow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(QueueList, source) is ListBoxItem { Content: SongRow row })
            await PlayRowAsync(row);
    }

    private async void ActivateRowFromKeyboard(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await PlayRowAsync(QueueList.SelectedItem as SongRow);
    }

    private void LoadRowArt(object sender, RoutedEventArgs e) => RefreshThumbnail((Image)sender);
    private void UnloadRowArt(object sender, RoutedEventArgs e) => SongThumbnailLoader.Reset((Image)sender);
    private void ChangeRowArt(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Image { IsLoaded: true } image) RefreshThumbnail(image);
    }
    private void RefreshThumbnail(Image image) => thumbnails.Refresh(image, Model, (ImageSource)Resources["LibraryArtwork"]);

    private void OpenMoreOptions(object sender, RoutedEventArgs e)
    {
        if (MoreActions.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreActions;
            menu.IsOpen = true;
        }
    }

    private void OpenFileLocation(object sender, RoutedEventArgs e)
    {
        var path = Model?.CoverSong?.FilePath;
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void CopyFilePath(object sender, RoutedEventArgs e)
    {
        var path = Model?.CoverSong?.FilePath;
        if (!string.IsNullOrWhiteSpace(path))
            Clipboard.SetText(path);
    }
}
