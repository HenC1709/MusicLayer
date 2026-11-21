using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MusicLayer.Presentation.Wpf.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class LyricsView : UserControl
{
    private LyricsViewModel? model;

    public LyricsView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach(DataContext as LyricsViewModel);
    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) Attach(e.NewValue as LyricsViewModel);
    }

    private void Attach(LyricsViewModel? value)
    {
        if (model is not null) model.PropertyChanged -= OnLyricsChanged;
        model = value;
        if (model is not null) model.PropertyChanged += OnLyricsChanged;
        FollowButton.Visibility = Visibility.Collapsed;
        ScrollToActive();
    }

    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.Rows))
        {
            FollowButton.Visibility = Visibility.Collapsed;
            PlainScroll.ScrollToTop();
        }
        if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex)) ScrollToActive();
    }

    private void ScrollToActive()
    {
        if (FollowButton.Visibility == Visibility.Visible || model is not { ActiveLineIndex: >= 0 } lyrics) return;
        LinesList.ScrollIntoView(lyrics.Rows[lyrics.ActiveLineIndex]);
    }

    private void ResumeFollowing(object sender, RoutedEventArgs e)
    {
        FollowButton.Visibility = Visibility.Collapsed;
        ScrollToActive();
    }

    private async void SeekToVerse(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LyricRow row } && model is not null)
            await model.SeekToLineAsync(row);
    }

    private void ManualWheel(object sender, MouseWheelEventArgs e) => FollowButton.Visibility = Visibility.Visible;
    private void ManualTouch(object sender, TouchEventArgs e) => FollowButton.Visibility = Visibility.Visible;
    private void ManualKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            FollowButton.Visibility = Visibility.Visible;
    }

    private void ManualClick(object sender, MouseButtonEventArgs e)
    {
        for (var target = e.OriginalSource as Visual; target is not null; target = VisualTreeHelper.GetParent(target) as Visual)
        {
            if (target is ScrollBar) { FollowButton.Visibility = Visibility.Visible; break; }
            if (target == LinesList) break;
        }
    }
}
