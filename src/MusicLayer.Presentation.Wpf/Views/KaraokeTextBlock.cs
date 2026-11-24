using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MusicLayer.Presentation.Wpf.Views;

public sealed class KaraokeTextBlock : TextBlock
{
    public static readonly DependencyProperty VerseTextProperty = DependencyProperty.Register(
        nameof(VerseText), typeof(string), typeof(KaraokeTextBlock),
        new PropertyMetadata(string.Empty, UpdateHighlight));

    public static readonly DependencyProperty LitTextLengthProperty = DependencyProperty.Register(
        nameof(LitTextLength), typeof(int), typeof(KaraokeTextBlock),
        new PropertyMetadata(0, UpdateHighlight));

    private static readonly Brush HighlightBrush = CreateHighlightBrush();
    private readonly Run lit = new() { Foreground = HighlightBrush };
    private readonly Run remaining = new();

    public KaraokeTextBlock()
    {
        Inlines.Add(lit);
        Inlines.Add(remaining);
    }

    public string VerseText
    {
        get => (string)GetValue(VerseTextProperty);
        set => SetValue(VerseTextProperty, value);
    }

    public int LitTextLength
    {
        get => (int)GetValue(LitTextLengthProperty);
        set => SetValue(LitTextLengthProperty, value);
    }

    private static void UpdateHighlight(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var verse = (KaraokeTextBlock)sender;
        var text = verse.VerseText ?? string.Empty;
        var length = Math.Clamp(verse.LitTextLength, 0, text.Length);
        verse.lit.Text = text[..length];
        verse.remaining.Text = text[length..];
    }

    private static Brush CreateHighlightBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(218, 255, 246));
        brush.Freeze();
        return brush;
    }
}
