using System.Globalization;

namespace MusicLayer.Application.UseCases.Lyrics;

public sealed record TimedLyricLine(TimeSpan Start, string Text);

public static class LrcParser
{
    public static IReadOnlyList<TimedLyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return [];

        using var reader = new StringReader(lrc);
        var lines = new List<(long Milliseconds, string Text)>();
        var timestamps = new List<long>();
        var offset = 0;

        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.AsSpan().Trim().TrimStart('\uFEFF').TrimStart();
            if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase) && line.EndsWith("]"))
            {
                if (int.TryParse(line[8..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOffset))
                    offset = parsedOffset;
                continue;
            }

            timestamps.Clear();
            while (TryReadTimestamp(line, out var consumed, out var milliseconds))
            {
                timestamps.Add(milliseconds);
                line = line[consumed..].TrimStart();
            }

            if (timestamps.Count == 0)
                continue;

            // An empty timed line marks a gap and clears the active lyric.
            var text = line.ToString();
            foreach (var timestamp in timestamps)
                lines.Add((timestamp, text));
        }

        // A positive LRC offset makes lyrics appear earlier, wherever the tag occurs.
        return Array.AsReadOnly(lines.OrderBy(line => line.Milliseconds)
            .Select(line => new TimedLyricLine(TimeSpan.FromMilliseconds(line.Milliseconds - offset), line.Text))
            .ToArray());
    }

    private static bool TryReadTimestamp(ReadOnlySpan<char> line, out int consumed, out long milliseconds)
    {
        consumed = 0;
        milliseconds = 0;
        if (line.IsEmpty || line[0] != '[')
            return false;

        var end = line.IndexOf(']');
        if (end < 0)
            return false;

        var timestamp = line[1..end];
        var colon = timestamp.IndexOf(':');
        if (colon < 0 || !TryParseNumber(timestamp[..colon], out var minutes))
            return false;

        var secondsText = timestamp[(colon + 1)..];
        var separator = secondsText.IndexOfAny('.', ':');
        var fraction = 0;
        if (separator >= 0)
        {
            var digits = secondsText[(separator + 1)..];
            if (digits.Length is < 1 or > 3 || !TryParseNumber(digits, out fraction))
                return false;
            fraction *= digits.Length switch { 1 => 100, 2 => 10, _ => 1 };
            secondsText = secondsText[..separator];
        }

        if (secondsText.Length > 2 || !TryParseNumber(secondsText, out var seconds) || seconds >= 60)
            return false;

        milliseconds = (minutes * 60L + seconds) * 1000 + fraction;
        consumed = end + 1;
        return true;
    }

    private static bool TryParseNumber(ReadOnlySpan<char> text, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
