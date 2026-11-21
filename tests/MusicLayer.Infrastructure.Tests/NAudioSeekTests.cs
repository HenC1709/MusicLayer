using MusicLayer.Infrastructure.Audio;
using Xunit;

namespace MusicLayer.Infrastructure.Tests;

public sealed class NAudioSeekTests
{
    [Theory]
    [InlineData(1, -2000)]
    [InlineData(2, 3000)]
    public void OpenReaderAtPositionReturnsPcmFromRequestedFlacFrame(int frame, short expected)
    {
        // 44.1 kHz mono FLAC without a seek table. Each frame has 4096 samples at a distinct level.
        var flac = Convert.FromBase64String(
            "ZkxhQ4AAACIQABAAAAAAAAAACsRA8AAAMAAAAAAAAAAAAAAAAAAAAAAA//jACACvAAPoaFf/+MAIAagA+DAc8//4wAgCoQALuAFE");
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.FLAC");
        try
        {
            File.WriteAllBytes(path, flac);
            var target = TimeSpan.FromSeconds((frame * 4096 + 512) / 44100.0);
            using var reader = NAudioPlayer.OpenReaderAtPosition(path, target, CancellationToken.None);
            var pcm = new byte[reader.BlockAlign];
            Assert.Equal(pcm.Length, reader.Read(pcm, 0, pcm.Length));
            Assert.Equal(expected, BitConverter.ToInt16(pcm));
        }
        finally { File.Delete(path); }
    }
}
