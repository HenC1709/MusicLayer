using MusicLayer.Shared.Metadata;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class TagLibCoverArtReaderTests
{
    [Theory]
    [InlineData(".FLAC")]
    [InlineData(".MP3")]
    [InlineData(".WAV")]
    public void ReadsEmbeddedPicturesInCoverOrderAndReleasesTheFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
        try
        {
            AudioFixture.Create(path);
            var reader = new TagLibCoverArtReader();
            Assert.Null(reader.ReadEmbeddedCover(path));
            var back = new TagLib.Picture
            {
                Type = TagLib.PictureType.BackCover, MimeType = "image/gif",
                Data = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==")
            };
            var front = new TagLib.Picture
            {
                Type = TagLib.PictureType.FrontCover, MimeType = "image/png",
                Data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAQSURBVBhXY/jPwPCfARkAAB7zAf+x9MCaAAAAAElFTkSuQmCC")
            };
            void SavePictures(params TagLib.IPicture[] pictures)
            {
                using var file = TagLib.File.Create(path);
                file.Tag.Pictures = pictures;
                file.Save();
            }

            SavePictures(back);
            Assert.Equal(back.Data.Data, reader.ReadEmbeddedCover(path));
            SavePictures(back, front);
            Assert.Equal(front.Data.Data, reader.ReadEmbeddedCover(path));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally { File.Delete(path); }
    }
}
