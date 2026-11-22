using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicLayer.Presentation.Wpf.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class LibraryView : UserControl
{
    private readonly SemaphoreSlim thumbnailSlots = new(3);
    private readonly List<string> history = ["Canciones"];
    private int historyIndex;
    private bool navigating;
    private MainViewModel? Model => DataContext as MainViewModel;

    public static readonly DependencyProperty CanGoBackProperty =
        DependencyProperty.Register(nameof(CanGoBack), typeof(bool), typeof(LibraryView));
    public static readonly DependencyProperty CanGoForwardProperty =
        DependencyProperty.Register(nameof(CanGoForward), typeof(bool), typeof(LibraryView));
    public bool CanGoBack => (bool)GetValue(CanGoBackProperty);
    public bool CanGoForward => (bool)GetValue(CanGoForwardProperty);
    public event RoutedEventHandler? ImportRequested;

    public LibraryView()
    {
        InitializeComponent();
        CategoryTabs.SelectionChanged += CategoryChanged;
        SongList.SelectionChanged += (_, _) =>
        {
            if (IsVisible && SongList.SelectedItem is SongRow row) SongList.ScrollIntoView(row);
        };
    }

    private void CategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (navigating || CategoryTabs.SelectedItem is not string section || history[historyIndex] == section) return;
        history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        history.Add(section);
        historyIndex++;
        UpdateHistory();
    }

    private void GoBack(object sender, RoutedEventArgs e) => Navigate(-1);
    private void GoForward(object sender, RoutedEventArgs e) => Navigate(1);
    private void Navigate(int direction)
    {
        int next = historyIndex + direction;
        if (next < 0 || next >= history.Count || Model is not { } model) return;
        navigating = true;
        try { historyIndex = next; model.Library.Section = history[next]; }
        finally { navigating = false; }
        UpdateHistory();
    }

    private void UpdateHistory()
    {
        SetValue(CanGoBackProperty, historyIndex > 0);
        SetValue(CanGoForwardProperty, historyIndex + 1 < history.Count);
    }

    internal void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }
    internal void FocusSongList()
    {
        SongList.Focus();
        var item = SongList.SelectedItem ?? SongList.Items.Cast<object>().FirstOrDefault();
        if (item is null) return;
        SongList.ScrollIntoView(item);
        SongList.UpdateLayout();
        (SongList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
    }

    private void ImportFolder(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, e);
    private void OpenSortMenu(object sender, RoutedEventArgs e)
    {
        var menu = SortButton.ContextMenu;
        menu.PlacementTarget = SortButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void ChooseSort(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Header: string sort } && Model is { } model) model.Library.SortOrder = sort;
    }

    private async Task PlayRowAsync(SongRow? row)
    {
        if (row is null || Model is not { IsReady: true } model) return;
        model.SelectedLibraryRow = row;
        await model.PlaySelectedAsync();
    }
    private async void PlayVisible(object sender, RoutedEventArgs e)
        => await PlayRowAsync(Model?.Library.Rows.FirstOrDefault());
    private async void PlayRandom(object sender, RoutedEventArgs e)
    {
        if (Model?.Library.Rows is { Count: > 0 } rows) await PlayRowAsync(rows[Random.Shared.Next(rows.Count)]);
    }
    private async void ActivateRow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(SongList, source) is not ListBoxItem { Content: SongRow row } item) return;
        for (var node = source; node is Visual && node != item; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase) return;
        await PlayRowAsync(row);
    }
    private async void ActivateRowFromKeyboard(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is ButtonBase) return;
        e.Handled = true;
        await PlayRowAsync(SongList.SelectedItem as SongRow);
    }

    private void ResizeView(object sender, SizeChangedEventArgs e)
    {
        double width = e.NewSize.Width;
        bool compact = width < 760;
        bool shortView = e.NewSize.Height < 470;
        bool compactTabs = compact && shortView;
        CategoryTabs.Visibility = compactTabs ? Visibility.Collapsed : Visibility.Visible;
        CompactCategory.Visibility = compactTabs ? Visibility.Visible : Visibility.Collapsed;
        Summary.Visibility = shortView ? Visibility.Collapsed : Visibility.Visible;
        Layout.Margin = compact ? new Thickness(12,8,12,4) : new Thickness(24,18,20,8);
        Breadcrumb.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible;
        Shortcut.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Detail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DetailColumn.Width = compact ? new GridLength(0) : new GridLength(.4, GridUnitType.Star);
        ArtFrame.Height = Math.Clamp((width - 48) / 3.5 - 65, 112, 268);
        Heading.Margin = shortView ? new Thickness(0,4,0,3) : new Thickness(0,17,0,14);
        Title.FontSize = shortView ? 23 : 38;
        Toolbar.Margin = shortView ? new Thickness(0,0,0,3) : new Thickness(0,6,0,10);
        SortLabel.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible;
        Resources["ArtistColumn"] = new GridLength(1, GridUnitType.Star);
        Resources["AlbumColumn"] = width < 660 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Resources["TimeColumn"] = new GridLength(52);
    }

    private void LoadRowArt(object sender, RoutedEventArgs e) => RefreshThumbnail((Image)sender);
    private void UnloadRowArt(object sender, RoutedEventArgs e) => ResetThumbnail((Image)sender);
    private void ChangeRowArt(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Image { IsLoaded: true } image) RefreshThumbnail(image);
    }

    private static void ResetThumbnail(Image image)
    {
        (image.Tag as CancellationTokenSource)?.Cancel();
        image.Tag = null;
        image.Source = null;
    }

    private async void RefreshThumbnail(Image image)
    {
        ResetThumbnail(image);
        if (image.DataContext is not SongRow row || Model is not { } model) return;
        image.Source = (BitmapSource)Resources["LibraryArtwork"];
        using var request = new CancellationTokenSource();
        image.Tag = request;
        try
        {
            await thumbnailSlots.WaitAsync(request.Token);
            try
            {
                var art = await model.ReadThumbnailAsync(row.Song, request.Token);
                if (!request.IsCancellationRequested && art is not null) image.Source = art;
            }
            finally { thumbnailSlots.Release(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { /* An unreadable embedded picture keeps the fallback artwork. */ }
        finally { if (image.Tag == request) image.Tag = null; }
    }
}
