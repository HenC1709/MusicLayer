using MusicLayer.Application.UseCases.Library;
using MusicLayer.Infrastructure.FileSystem;
using MusicLayer.Infrastructure.Metadata;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class MusicFileScannerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "MusicLayerTests", Guid.NewGuid().ToString());
    private readonly MusicFileScanner scanner = new(new TagLibTrackMetadataReader());

    public MusicFileScannerTests() => Directory.CreateDirectory(Path.Combine(folder, "album"));

    [Fact]
    public async Task FindsNestedAudioAndIgnoresOtherFiles()
    {
        File.WriteAllText(Path.Combine(folder, "song.MP3"), "");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
        File.WriteAllText(Path.Combine(folder, "album", "track.flac"), "");

        var songs = await new ScanMusicFolder(scanner).ExecuteAsync(folder);

        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, s => s.DisplayName == "track");
        Assert.Contains(songs, s => s.DisplayName == "song");
    }

    [Fact]
    public async Task UnsupportedFolderIsEmptyAndMissingRootIsAnError()
    {
        File.WriteAllText(Path.Combine(folder, "notas.txt"), "texto");
        Assert.Empty(await scanner.ScanAsync(folder));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => scanner.ScanAsync(Path.Combine(folder, "ausente")));
    }

    [Fact]
    public async Task CancelledTokenStopsScan()
    {
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(folder, new CancellationToken(true)));
    }

    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    [InlineData("flac")]
    public async Task ReadsMetadataWithoutSkippingUntaggedOrCorruptTracks(string extension)
    {
        var taggedPath = Path.Combine(folder, $"01-etiquetada.{extension}");
        var partialPath = Path.Combine(folder, $"02-titulo-ausente.{extension}");
        var plainPath = Path.Combine(folder, $"03-sin-etiquetas.{extension}");
        var corruptPath = Path.Combine(folder, $"04-dañada.{extension}");

        CreateAudioFile(taggedPath, extension);
        File.Copy(taggedPath, partialPath);
        File.Copy(taggedPath, plainPath);
        File.WriteAllText(corruptPath, "Archivo de audio inválido.");
        WriteTags(taggedPath, " Zéfiro ", " Lía ", " Azul ");
        WriteTags(partialPath, null, "Lía", null);

        var songs = await new ScanMusicFolder(scanner).ExecuteAsync(folder);
        Assert.Collection(songs,
            song =>
            {
                Assert.Equal(taggedPath, song.FilePath);
                Assert.Equal("Zéfiro", song.DisplayName);
                Assert.Equal("Lía", song.Metadata?.Artist);
                Assert.Equal("Azul", song.Metadata?.Album);
            },
            song =>
            {
                Assert.Equal("02-titulo-ausente", song.DisplayName);
                Assert.Null(song.Metadata?.Title);
                Assert.Equal("Lía", song.Metadata?.Artist);
                Assert.Null(song.Metadata?.Album);
            },
            song =>
            {
                Assert.Equal("03-sin-etiquetas", song.DisplayName);
                Assert.Null(song.Metadata);
            },
            song =>
            {
                Assert.Equal("04-dañada", song.DisplayName);
                Assert.Null(song.Metadata);
            });

        using var exclusive = File.Open(taggedPath, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    private static void WriteTags(string path, string? title, string artist, string? album)
    {
        using var file = TagLib.File.Create(path, TagLib.ReadStyle.None);
        file.Tag.Title = title;
        file.Tag.Performers = [artist];
        file.Tag.Album = album;
        file.Save();
    }

    private static void CreateAudioFile(string path, string extension)
    {
        if (extension == "flac")
        {
            // One frame of silence at 44.1 kHz, mono.
            File.WriteAllBytes(path, Convert.FromHexString(
                "664c614380000022100010000000000000000ac440f00000100000000000000000000000000000000000fff8c908009500000021bd"));
            return;
        }

        using var writer = new BinaryWriter(File.Create(path));
        if (extension == "mp3")
        {
            // MPEG-1 Layer III frames at 128 kbps, 44.1 kHz, without ID3 tags.
            byte[] frame = new byte[417];
            new byte[] { 0xFF, 0xFB, 0x90, 0xC4 }.CopyTo(frame, 0);
            for (var i = 0; i < 3; i++) writer.Write(frame);
            return;
        }

        const int dataSize = 16;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    public void Dispose() => Directory.Delete(folder, true);
}
