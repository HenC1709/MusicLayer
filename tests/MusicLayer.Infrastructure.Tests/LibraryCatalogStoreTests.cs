using MusicLayer.Domain.Entities;
using MusicLayer.Shared.Library;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class LibraryCatalogStoreTests
{
    [Fact]
    public async Task RestoresImportedTracksInASeparateSessionAndReplacesThePreviousSelection()
    {
        var folder = Directory.CreateTempSubdirectory("tonic-library-");
        string path = Path.Combine(folder.FullName, "library.json");
        try
        {
            Assert.Empty(await new LibraryCatalogStore(path).LoadAsync());
            LibraryCatalogTrack[] imported =
            [
                new("Pista.flac", new TrackMetadata("Título", "Artista", "Álbum")
                {
                    Duration = TimeSpan.FromSeconds(45)
                }, "cover.jpg") { ContentUri = "content://example/audio/1" },
                new("Sin etiquetas.wav", null, null)
            ];
            await new LibraryCatalogStore(path).SaveAsync(imported);
            Assert.Equal(imported, await new LibraryCatalogStore(path).LoadAsync());

            LibraryCatalogTrack[] replacement = [new("Otra.mp3", null, null)];
            await new LibraryCatalogStore(path).SaveAsync(replacement);
            Assert.Equal(replacement, await new LibraryCatalogStore(path).LoadAsync());
            Assert.Single(folder.GetFiles());
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task LoadsCatalogCreatedBeforePlaybackWasAdded()
    {
        var folder = Directory.CreateTempSubdirectory("tonic-library-");
        string path = Path.Combine(folder.FullName, "library.json");
        try
        {
            await File.WriteAllTextAsync(path, """[{"FileName":"Anterior.flac","Metadata":null,"CoverFileName":null}]""");
            var track = Assert.Single(await new LibraryCatalogStore(path).LoadAsync());
            Assert.Equal("Anterior.flac", track.FileName);
            Assert.Null(track.ContentUri);
        }
        finally { folder.Delete(recursive: true); }
    }
}
