using System.Windows;
using System.Windows.Media;
using MusicLayer.Infrastructure.Audio;

namespace MusicLayer.Presentation.Wpf.Views;

public sealed class TrackWaveform : FrameworkElement
{
    private static readonly Brush Played = FrozenBrush(0x63, 0xEE, 0xC8);
    private static readonly Brush Remaining = FrozenBrush(0x36, 0x68, 0x73);
    private CancellationTokenSource? request;
    private double[]? levels;

    public static readonly DependencyProperty TrackPathProperty = DependencyProperty.Register(
        nameof(TrackPath), typeof(string), typeof(TrackWaveform),
        new PropertyMetadata(null, (sender, _) => ((TrackWaveform)sender).ChangeTrack()));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TrackWaveform),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? TrackPath
    {
        get => (string?)GetValue(TrackPathProperty);
        set => SetValue(TrackPathProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public TrackWaveform() => IsVisibleChanged += (_, _) =>
    {
        if (IsVisible) LoadLevels();
        else CancelLoad();
    };

    private void ChangeTrack()
    {
        CancelLoad();
        levels = null;
        InvalidateVisual();
        LoadLevels();
    }

    private async void LoadLevels()
    {
        if (!IsVisible || TrackPath is not { Length: > 0 } path || request is not null || levels is not null)
            return;

        using var source = new CancellationTokenSource();
        request = source;
        try
        {
            var result = await TrackWaveformReader.ReadAsync(path, source.Token);
            if (request == source) levels = result;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception) { if (request == source) levels = []; }
        finally
        {
            if (request == source)
            {
                request = null;
                InvalidateVisual();
            }
        }
    }

    private void CancelLoad()
    {
        request?.Cancel();
        request = null;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 2) return;

        var progress = Math.Clamp(Progress, 0, 1) * width;
        var center = height / 2;
        drawingContext.DrawRectangle(Remaining, null, new Rect(0, center, width, 1));
        if (levels is { Length: > 0 } bands)
        {
            var step = width / bands.Length;
            var barWidth = Math.Max(1, step * .52);
            for (var i = 0; i < bands.Length; i++)
            {
                var level = double.IsFinite(bands[i]) ? bands[i] : 0;
                var barHeight = Math.Max(2, level * (height - 4));
                var x = (i + .5) * step;
                drawingContext.DrawRoundedRectangle(x <= progress ? Played : Remaining, null,
                    new Rect(x - barWidth / 2, center - barHeight / 2, barWidth, barHeight), 1.2, 1.2);
            }
        }
        else if (progress > 0)
        {
            drawingContext.DrawRectangle(Played, null, new Rect(0, center, progress, 1));
        }

        var position = Math.Min(progress, width - 2);
        drawingContext.DrawRoundedRectangle(Played, null, new Rect(position, 0, 2, height), 1, 1);
    }

    private static SolidColorBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
