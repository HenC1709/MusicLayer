using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        progressTimer.Tick += RefreshPlaybackProgress;
        progressTimer.Start();
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegir carpeta de música" };
        if (dialog.ShowDialog(this) == true) await viewModel.LoadFolderAsync(dialog.FolderName);
    }

    private async void PlaySelected(object sender, RoutedEventArgs e)
        => await viewModel.PlaySelectedAsync();

    private void ChooseAnotherSong(object sender, RoutedEventArgs e)
    {
        Library.IsChecked = true;
        SongList.Focus();
        var index = Math.Max(0, SongList.SelectedIndex);
        if (index >= SongList.Items.Count) return;
        SongList.ScrollIntoView(SongList.Items[index]);
        SongList.UpdateLayout();
        (SongList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem)?.Focus();
    }

    private async void PlayPrevious(object sender, RoutedEventArgs e)
        => await viewModel.PlayPreviousAsync();

    private async void PlayNext(object sender, RoutedEventArgs e)
        => await viewModel.PlayNextAsync();

    private async void TogglePlayback(object sender, RoutedEventArgs e)
        => await viewModel.TogglePlaybackAsync();

    private async void ActivateRow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(SongList, source) is ListBoxItem)
            await viewModel.PlaySelectedAsync();
    }

    private async void ActivateRowFromKeyboard(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !viewModel.CanPlaySelected) return;
        e.Handled = true;
        await viewModel.PlaySelectedAsync();
    }

    private async void Stop(object sender, RoutedEventArgs e)
        => await viewModel.StopAsync();

    private async void RefreshPlaybackProgress(object? sender, EventArgs e)
        => await viewModel.RefreshPlaybackProgressAsync();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (canClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (closing) return;
        closing = true;
        progressTimer.Stop();
        progressTimer.Tick -= RefreshPlaybackProgress;
        try
        {
            try { await viewModel.DisposeAsync(); }
            finally { await ((App)System.Windows.Application.Current).ReleaseServicesAsync(); }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"Error al liberar recursos: {error.Message}", "MusicLayer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { canClose = true; Close(); }
    }
}
