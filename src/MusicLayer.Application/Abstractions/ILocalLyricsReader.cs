namespace MusicLayer.Application.Abstractions;

public interface ILocalLyricsReader
{
    Task<string?> ReadAsync(string audioPath, CancellationToken cancellationToken);
}
