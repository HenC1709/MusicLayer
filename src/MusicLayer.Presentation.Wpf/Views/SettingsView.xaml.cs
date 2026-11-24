using System.Windows;
using System.Windows.Controls;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    public event RoutedEventHandler? ImportRequested;

    private void ChangeFolder(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, e);

    private void ShowFutureTheme(object sender, RoutedEventArgs e)
        => MessageBox.Show(Window.GetWindow(this), "en futuras versiones", $"Tema · {((Button)sender).Content}",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void ResizeSettings(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        bool compact = width < 730;
        bool spacious = !compact && e.NewSize.Height > 700;
        SettingsTitle.FontSize = spacious ? 38 : 32;
        SettingsBody.Margin = compact ? new Thickness(14, 14, 14, 6) : new Thickness(32, spacious ? 38 : 25, 32, 14);
        double vertical = spacious ? 23 : 10;
        Resources["CardPadding"] = compact ? new Thickness(12) : new Thickness(20, vertical, 20, vertical);
        Resources["LabelDock"] = compact ? Dock.Top : Dock.Left;
        Resources["LabelWidth"] = compact ? double.NaN : Math.Min(width * .39, 510);
        Resources["LabelMargin"] = compact ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 16, 0);
    }
}
