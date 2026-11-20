using MusicLayer.Application.UseCases.Library;
using MusicLayer.Infrastructure.FileSystem;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class MusicFileScannerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "MusicLayerTests", Guid.NewGuid().ToString());

    public MusicFileScannerTests() => Directory.CreateDirectory(Path.Combine(folder, "album"));

    [Fact]
    public async Task FindsNestedAudioAndIgnoresOtherFiles()
    {
        File.WriteAllText(Path.Combine(folder, "song.MP3"), "");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
        File.WriteAllText(Path.Combine(folder, "album", "track.flac"), "");

        var songs = await new ScanMusicFolder(new MusicFileScanner()).ExecuteAsync(folder);

        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, s => s.DisplayName == "track");
        Assert.Contains(songs, s => s.DisplayName == "song");
    }

    [Fact]
    public async Task UnsupportedFolderIsEmptyAndMissingRootIsAnError()
    {
        File.WriteAllText(Path.Combine(folder, "notas.txt"), "texto");
        var scanner = new MusicFileScanner();
        Assert.Empty(await scanner.ScanAsync(folder));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => scanner.ScanAsync(Path.Combine(folder, "ausente")));
    }

    [Fact]
    public async Task CancelledTokenStopsScan()
    {
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MusicFileScanner().ScanAsync(folder, new CancellationToken(true)));
    }

    public void Dispose() => Directory.Delete(folder, true);
}
