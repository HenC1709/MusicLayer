using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MusicLayer.Presentation.Wpf.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer progressTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private bool closing;
    private bool canClose;
    private bool draggingSeekThumb;
    private bool updatingSeekSlider;
    private bool submittingSeek;
    private double seekDragStart;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        TonicLogo.Source = (Icon as BitmapFrame)?.Decoder?.Frames.MaxBy(frame => frame.PixelWidth) ?? Icon;
        this.viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        DataContext = viewModel;
        Loaded += RestorePreferences;
        SizeChanged += (_, _) =>
        {
            bool compact = ActualWidth < 850;
            RailColumn.Width = new GridLength(compact ? 62 : Math.Min(236, ActualWidth * .15));
            Brand.Visibility = FolderInfo.Visibility = MenuButtonLabel.Visibility =
                compact ? Visibility.Collapsed : Visibility.Visible;
            bool shortWindow = ActualHeight < 530;
            NavigationPanel.Margin = new Thickness(10, shortWindow ? 10 : 54, 10, 10);
            var routePadding = shortWindow ? 5 : 8;
            Resources["MenuRoutePadding"] = new Thickness(12, routePadding, 12, routePadding);
        };
        PreviewKeyDown += HandleNavigationKeys;
        SyncSeekSlider();
        progressTimer.Tick += RefreshPlaybackProgress;
        progressTimer.Start();
    }

    private async void RestorePreferences(object sender, RoutedEventArgs e)
    {
        Loaded -= RestorePreferences;
        await viewModel.InitializeAsync();
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegir carpeta de música" };
        if (dialog.ShowDialog(this) == true) await viewModel.LoadFolderAsync(dialog.FolderName);
    }

    private void HandleNavigationKeys(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NavigationOverlay.IsVisible)
        {
            CloseNavigationMenu();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CloseNavigationMenu(restoreFocus: false);
            Library.IsChecked = true;
            LibraryScreen.FocusSearch();
            e.Handled = true;
        }
    }

    private void OpenNavigationMenu(object sender, RoutedEventArgs e)
    {
        NavigationOverlay.Visibility = Visibility.Visible;
        SetScreenTabNavigation(KeyboardNavigationMode.None);
        NavigationOverlay.UpdateLayout();
        var active = LyricsTab.IsChecked == true ? MenuLyrics
            : NowPlaying.IsChecked == true ? MenuNowPlaying
            : MenuLibrary;
        active.Focus();
    }

    private void CloseNavigationMenu(bool restoreFocus = true)
    {
        if (NavigationOverlay.Visibility != Visibility.Visible) return;
        NavigationOverlay.Visibility = Visibility.Collapsed;
        SetScreenTabNavigation(KeyboardNavigationMode.Continue);
        if (restoreFocus) NavigationMenuButton.Focus();
    }

    private void SetScreenTabNavigation(KeyboardNavigationMode mode)
    {
        KeyboardNavigation.SetTabNavigation(NavigationRail, mode);
        KeyboardNavigation.SetTabNavigation(ScreenHost, mode);
    }

    private void DismissNavigationMenu(object sender, RoutedEventArgs e) => CloseNavigationMenu();
    private void DismissNavigationBackdrop(object sender, MouseButtonEventArgs e) => CloseNavigationMenu();

    private void NavigateFromMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: RadioButton destination }) return;
        destination.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        CloseNavigationMenu();
    }

    private void ImportFromMenu(object sender, RoutedEventArgs e)
    {
        CloseNavigationMenu();
        ChooseFolder(sender, e);
    }

    private void ShowLyrics(object sender, RoutedEventArgs e) => LyricsTab.IsChecked = true;

    private void ChooseAnotherSong(object sender, RoutedEventArgs e)
    {
        Library.IsChecked = true;
        LibraryScreen.FocusSongList();
    }

    private async void RetryPlayback(object sender, RoutedEventArgs e)
        => await viewModel.RetryPlaybackAsync();

    private async void PlayPrevious(object sender, RoutedEventArgs e)
        => await viewModel.PlayPreviousAsync();

    private async void PlayNext(object sender, RoutedEventArgs e)
        => await viewModel.PlayNextAsync();

    private async void PlayRandomNext(object sender, RoutedEventArgs e)
        => await viewModel.PlayRandomNextAsync();

    private async void TogglePlayback(object sender, RoutedEventArgs e)
        => await viewModel.TogglePlaybackAsync();

    private async void Stop(object sender, RoutedEventArgs e)
        => await viewModel.StopAsync();

    private async void RefreshPlaybackProgress(object? sender, EventArgs e)
        => await viewModel.RefreshPlaybackProgressAsync();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ProgressFraction)) SyncSeekSlider();
    }

    private void SyncSeekSlider()
    {
        if (draggingSeekThumb || submittingSeek) return;
        updatingSeekSlider = true;
        try { SeekSlider.Value = viewModel.ProgressFraction; }
        finally { updatingSeekSlider = false; }
    }

    private async void SeekSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingSeekSlider && !draggingSeekThumb && SeekSlider.IsEnabled)
            await CommitSeekAsync(e.NewValue);
    }

    private void SeekSliderEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (IsLoaded) SyncSeekSlider();
            return;
        }

        if (!draggingSeekThumb) return;
        draggingSeekThumb = false;
        var slider = (Slider)sender;
        if (slider.Template?.FindName("PART_Track", slider) is Track track)
            track.Thumb?.CancelDrag();
    }

    private void SeekDragStarted(object sender, DragStartedEventArgs e)
    {
        draggingSeekThumb = SeekSlider.IsEnabled && !submittingSeek;
        seekDragStart = SeekSlider.Value;
    }

    private async void SeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!draggingSeekThumb) return;
        draggingSeekThumb = false;
        if (e.Canceled || SeekSlider.Value == seekDragStart) SyncSeekSlider();
        else await CommitSeekAsync(SeekSlider.Value);
    }

    private async Task CommitSeekAsync(double fraction)
    {
        if (submittingSeek) return;
        submittingSeek = true;
        try { await viewModel.SeekAsync(fraction); }
        finally
        {
            submittingSeek = false;
            SyncSeekSlider();
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (canClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (closing) return;
        closing = true;
        progressTimer.Stop();
        progressTimer.Tick -= RefreshPlaybackProgress;
        viewModel.PropertyChanged -= OnViewModelChanged;
        try
        {
            try { await viewModel.DisposeAsync(); }
            finally { await ((App)System.Windows.Application.Current).ReleaseServicesAsync(); }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"Error al liberar recursos: {error.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            await Dispatcher.Yield();
            canClose = true;
            Close();
        }
    }
}
