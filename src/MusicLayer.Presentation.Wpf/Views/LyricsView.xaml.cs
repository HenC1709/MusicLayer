using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicLayer.Presentation.Wpf.ViewModels;

namespace MusicLayer.Presentation.Wpf.Views;

public partial class LyricsView : UserControl
{
    private LyricsViewModel? model;
    private DispatcherOperation? pendingFollow;
    private readonly DispatcherTimer scrollTimer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };
    private readonly DispatcherTimer manualScrollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ScrollViewer? scrolling;
    private double startOffset;
    private double targetOffset;
    private long scrollStarted;

    public static readonly DependencyProperty AmbientCoverProperty = DependencyProperty.Register(
        nameof(AmbientCover), typeof(BitmapSource), typeof(LyricsView),
        new PropertyMetadata(null, (view, change) => ((LyricsView)view).UpdateAmbient((BitmapSource?)change.NewValue)));

    public BitmapSource? AmbientCover
    {
        get => (BitmapSource?)GetValue(AmbientCoverProperty);
        set => SetValue(AmbientCoverProperty, value);
    }

    public LyricsView()
    {
        InitializeComponent();
        scrollTimer.Tick += AnimateScroll;
        manualScrollTimer.Tick += ResumeAfterScrolling;
        IsVisibleChanged += (_, _) =>
        {
            ResetFollowing();
            FollowActive();
        };
    }

    private void UpdateAmbient(BitmapSource? cover)
    {
        var tint = cover is null ? Color.FromRgb(54, 223, 231) : SampleCover(cover);
        TransitionTo(AmbientGlow, Color.FromRgb(
            (byte)(10 + .13 * tint.R), (byte)(19 + .13 * tint.G), (byte)(28 + .13 * tint.B)));
        TransitionTo(AmbientShade, Color.FromRgb(
            (byte)(8 + .06 * tint.R), (byte)(19 + .06 * tint.G), (byte)(27 + .06 * tint.B)));
    }

    private static Color SampleCover(BitmapSource cover)
    {
        var sample = new TransformedBitmap(cover, new ScaleTransform(16d / cover.PixelWidth, 16d / cover.PixelHeight));
        var bitmap = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        double red = 0, green = 0, blue = 0, total = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            var weight = pixels[i + 3] * (chroma + 24d);
            red += r * weight;
            green += g * weight;
            blue += b * weight;
            total += weight;
        }

        return total == 0 ? Colors.Transparent :
            Color.FromRgb((byte)(red / total), (byte)(green / total), (byte)(blue / total));
    }

    private void TransitionTo(GradientStop stop, Color target)
    {
        if (Equals(stop.GetAnimationBaseValue(GradientStop.ColorProperty), target)) return;
        var from = stop.Color;
        stop.Color = target;
        var animation = IsVisible && SystemParameters.ClientAreaAnimation
            ? new ColorAnimation(from, target, TimeSpan.FromMilliseconds(650)) { FillBehavior = FillBehavior.Stop }
            : null;
        stop.BeginAnimation(GradientStop.ColorProperty, animation);
    }

    private void ResizeComposition(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        var compact = width < 700 || height < 370;
        var gutter = compact ? 14 : Math.Clamp(width * .035, 22, 52);
        var cover = compact ? 46 : Math.Min(Math.Min(width * .245, height * .50), 350);

        DockPanel.SetDock(TrackPanel, compact ? Dock.Top : Dock.Left);
        TrackPanel.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        TrackPanel.Margin = compact ? new Thickness(gutter, 10, gutter, 0) : new Thickness(gutter, 0, gutter, 0);
        Artwork.Width = Artwork.Height = cover;
        TrackInfo.Margin = compact ? new Thickness(12, 0, 0, 0) : new Thickness(0, 16, 0, 0);
        TrackInfo.Width = compact ? Math.Max(0, width - 2 * gutter - cover - 12) : cover;
        VersePanel.Margin = new Thickness(gutter, compact ? 8 : gutter, gutter, compact ? 6 : gutter);
        Resources["VerseSize"] = compact ? 20d : Math.Clamp(width * .028, 24, 38);
        Resources["ActiveVerseSize"] = compact ? 24d : Math.Clamp(width * .036, 30, 50);
        FollowActive();
    }

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
        ResetFollowing();
        FollowActive();
    }

    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.Rows))
        {
            ResetFollowing();
            PlainScroll.ScrollToTop();
            FindScrollViewer(LinesList)?.ScrollToTop();
        }
        if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex)) FollowActive();
    }

    private void FollowActive()
    {
        StopFollowingMotion();
        if (!IsLoaded || !IsVisible || manualScrollTimer.IsEnabled ||
            model is not { ActiveLineIndex: >= 0 } lyrics) return;

        var row = lyrics.Rows[lyrics.ActiveLineIndex];
        pendingFollow = Dispatcher.InvokeAsync(() => PositionVerse(row), DispatcherPriority.Loaded);
    }

    private void PositionVerse(LyricRow row)
    {
        pendingFollow = null;
        if (FindScrollViewer(LinesList) is not { ViewportHeight: > 0 } scroll) return;
        if (LinesList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement)
        {
            // A distant seek first needs the virtualizing panel to realize the target.
            LinesList.ScrollIntoView(row);
            LinesList.UpdateLayout();
        }
        if (LinesList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement verse) return;

        var viewport = scroll.ViewportHeight;
        var readingPosition = Math.Clamp(viewport - verse.ActualHeight, 0, viewport * .38);
        var top = verse.TranslatePoint(new Point(), scroll).Y;
        startOffset = scroll.VerticalOffset;
        targetOffset = Math.Clamp(startOffset + top - readingPosition, 0, scroll.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation || Math.Abs(targetOffset - startOffset) < 2)
        {
            scroll.ScrollToVerticalOffset(targetOffset);
            return;
        }

        scrolling = scroll;
        scrollStarted = Stopwatch.GetTimestamp();
        scrollTimer.Start();
    }

    private void AnimateScroll(object? sender, EventArgs e)
    {
        var progress = Math.Min(1, Stopwatch.GetElapsedTime(scrollStarted).TotalMilliseconds / 350);
        var eased = 1 - Math.Pow(1 - progress, 3);
        scrolling?.ScrollToVerticalOffset(startOffset + (targetOffset - startOffset) * eased);
        if (progress >= 1) StopFollowingMotion();
    }

    private void StopFollowingMotion()
    {
        pendingFollow?.Abort();
        pendingFollow = null;
        scrollTimer.Stop();
        scrolling = null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        }
        return null;
    }

    private void ResetFollowing()
    {
        StopFollowingMotion();
        manualScrollTimer.Stop();
    }

    private void ResumeAfterScrolling(object? sender, EventArgs e)
    {
        if (Mouse.Captured is DependencyObject captured && LinesList.IsAncestorOf(captured)) return;
        manualScrollTimer.Stop();
        FollowActive();
    }

    private async void SeekToVerse(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LyricRow row } && model is not null)
        {
            manualScrollTimer.Stop();
            await model.SeekToLineAsync(row);
            FollowActive();
        }
    }

    private void PauseFollowing()
    {
        StopFollowingMotion();
        manualScrollTimer.Stop();
        manualScrollTimer.Start();
    }

    private void ManualWheel(object sender, MouseWheelEventArgs e) => PauseFollowing();
    private void ManualTouch(object sender, TouchEventArgs e) => PauseFollowing();
    private void ManualKey(object sender, KeyEventArgs e)
    {
        if (FindScrollViewer(LinesList) is not { } scroll) return;
        switch (e.Key)
        {
            case Key.Up: scroll.LineUp(); break;
            case Key.Down: scroll.LineDown(); break;
            case Key.PageUp: scroll.PageUp(); break;
            case Key.PageDown: scroll.PageDown(); break;
            case Key.Home: scroll.ScrollToTop(); break;
            case Key.End: scroll.ScrollToBottom(); break;
            default: return;
        }
        PauseFollowing();
        e.Handled = true;
    }

    private void ManualClick(object sender, MouseButtonEventArgs e)
    {
        for (var target = e.OriginalSource as Visual; target is not null; target = VisualTreeHelper.GetParent(target) as Visual)
        {
            if (target is ScrollBar) { PauseFollowing(); break; }
            if (target == LinesList) break;
        }
    }
}
