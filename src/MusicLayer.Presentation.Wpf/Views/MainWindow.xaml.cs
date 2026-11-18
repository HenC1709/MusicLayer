using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using MusicLayer.Presentation.Wpf.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool closing;
    private bool canClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegir carpeta de música" };
        if (dialog.ShowDialog(this) == true) await viewModel.LoadFolderAsync(dialog.FolderName);
    }

    private async void PlaySelected(object sender, RoutedEventArgs e)
        => await viewModel.PlaySelectedAsync();

    private async void PlayPrevious(object sender, RoutedEventArgs e)
        => await viewModel.PlayPreviousAsync();

    private async void PlayNext(object sender, RoutedEventArgs e)
        => await viewModel.PlayNextAsync();

    private async void Pause(object sender, RoutedEventArgs e)
        => await viewModel.PauseAsync();

    private async void Resume(object sender, RoutedEventArgs e)
        => await viewModel.ResumeAsync();

    private async void Stop(object sender, RoutedEventArgs e)
        => await viewModel.StopAsync();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (canClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (closing) return;
        closing = true;
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
