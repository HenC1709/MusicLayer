using MusicLayer.Infrastructure.Metadata;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class TagLibCoverArtReaderTests
{
    [Fact]
    public void ReadsEmbeddedFlacPicturesInCoverOrderAndReleasesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.FLAC");
        // A 44.1 kHz mono FLAC containing a single frame of silence and no pictures.
        File.WriteAllBytes(path, Convert.FromHexString(
            "664c614380000022100010000000000000000ac440f00000100000000000000000000000000000000000fff8c908009500000021bd"));
        try
        {
            var reader = new TagLibCoverArtReader();
            Assert.Null(reader.ReadEmbeddedCover(path));
            var back = new TagLib.Picture { Type = TagLib.PictureType.BackCover, MimeType = "image/jpeg", Data = new byte[] { 1, 2 } };
            var front = new TagLib.Picture { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg", Data = new byte[] { 3, 4 } };
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
