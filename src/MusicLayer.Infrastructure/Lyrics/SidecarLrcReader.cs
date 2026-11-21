using MusicLayer.Application.Abstractions;

namespace MusicLayer.Infrastructure.Lyrics;

public sealed class SidecarLrcReader : ILocalLyricsReader
{
    public async Task<string?> ReadAsync(string audioPath, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(Path.ChangeExtension(audioPath, ".lrc"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
